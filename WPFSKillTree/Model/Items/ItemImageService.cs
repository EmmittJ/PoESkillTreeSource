﻿using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Reactive.Concurrency;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NLog;
using PoESkillTree.Engine.GameModel.Items;
using PoESkillTree.Utils;
using PoESkillTree.Utils.Extensions;
using PoESkillTree.Utils.WikiApi;
using PoESkillTree.Utils.Wpf;

namespace PoESkillTree.Model.Items
{
    /// <summary>
    /// Serves item images for ItemGroups, base items and items with an icon url in their json.
    /// Each image will only have one task associated with it, independent on how many items load it.
    /// </summary>
    public class ItemImageService
    {
        private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Path to images for ItemGroups
        /// </summary>
        private const string ResourcePathFormat =
            "pack://application:,,,/PoESkillTree;component/Images/EquipmentUI/ItemDefaults/{0}.png";

        /// <summary>
        /// Path to images for base items.
        /// </summary>
        private static readonly string AssetPath =
            Path.Combine(AppData.GetFolder(), "Data", "Equipment", "Assets");
        private static readonly string AssetPathFormat = 
            Path.Combine(AssetPath, "{0}.png");

        /// <summary>
        /// Path to images downloaded from item json information.
        /// </summary>
        private static readonly string DownloadedPathFormat =
            Path.Combine(AppData.GetFolder(), "Data", "Equipment", "Downloaded", "{0}");

        private readonly HttpClient _httpClient = new HttpClient();
        private readonly PoolingImageLoader _wikiLoader;
        private readonly Options _options;
        private readonly IScheduler _scheduler;

        /// <summary>
        /// Stores images for ItemClasses.
        /// </summary>
        private readonly ConcurrentDictionary<ItemClass, ImageSource> _defaultImageCache = 
            new ConcurrentDictionary<ItemClass, ImageSource>();

        /// <summary>
        /// Stores tasks for images for base items. The key is the file name as inserted into
        /// <see cref="AssetPathFormat"/>.
        /// </summary>
        private readonly ConcurrentDictionary<string, Task<ImageSource>> _assetImageCache = 
            new ConcurrentDictionary<string, Task<ImageSource>>();

        /// <summary>
        /// Stores tasks for images downloaded from item json information. The key is the url of the image as stored in
        /// the json.
        /// </summary>
        private readonly ConcurrentDictionary<string, Task<ImageSource>> _downloadedImageCache =
            new ConcurrentDictionary<string, Task<ImageSource>>();

        /// <summary>
        /// Fallback image for ItemClasses.
        /// </summary>
        private readonly ImageSource _errorImage = Imaging.CreateBitmapSourceFromHIcon(
            SystemIcons.Error.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions()
        );

        public ItemImageService(Options options)
        {
            _options = options;
            _wikiLoader = new PoolingImageLoader(_httpClient);
            _scheduler = new EventLoopScheduler(a => new Thread(a) { Name = "ItemImageService", IsBackground = true });
            Directory.CreateDirectory(AssetPath);
        }

        /// <summary>
        /// Returns the image for the ItemClass. Each ItemClass image is only loaded once.
        /// </summary>
        public ImageSource LoadDefaultImage(ItemClass itemClass)
        {
            return _defaultImageCache.GetOrAdd(itemClass, c =>
            {
                if (Application.Current == null)
                    return _errorImage;
                try
                {
                    var path = string.Format(ResourcePathFormat, c);
                    return BitmapImageFactory.Create(path);
                }
                catch (Exception e)
                {
                    Log.Warn(e, "Could not load default file for ItemClass " + c);
                    return _errorImage;
                }
            });
        }

        /// <summary>
        /// Returns a task that returns the image for the base item with the given name. The task may already be
        /// completed if this method was already called with the same itemName parameter. Images may be downloaded
        /// from the game's wiki.
        /// </summary>
        /// <param name="itemName">name of the base item. Equals the asset file name of the image.</param>
        /// <param name="defaultImage">image the task returns if the image file does not exist and download of missing
        /// images is disabled.</param>
        public Task<ImageSource> LoadItemImageAsync(string itemName, Task<ImageSource> defaultImage)
            => _assetImageCache.GetOrAdd(itemName, n => Schedule(() => LoadAssetAsync(n, defaultImage)));

        private async Task<ImageSource> LoadAssetAsync(string itemName, Task<ImageSource> defaultImage)
        {
            var fileName = string.Format(AssetPathFormat, itemName);
            if (!File.Exists(fileName))
            {
                if (_options.DownloadMissingItemImages)
                    await _wikiLoader.ProduceAsync(itemName, fileName).ConfigureAwait(false);
                else
                    return await defaultImage.ConfigureAwait(false);
            }
            return BitmapImageFactory.Create(fileName);
        }

        /// <summary>
        /// Returns a task that returns the image at the given url. The task may already be
        /// completed if this method was already called with the same imageUrl parameter. Images are stored in the file
        /// system, the image will not be downloaded if it already was in a previous program run.
        /// </summary>
        /// <param name="imageUrl">url of the image. If it is a local url without domain, it is prefixed with
        /// pathofexile.com.</param>
        /// <param name="defaultImage">image the task returns if the image file does not exist and download of missing
        /// images is disabled.</param>
        public Task<ImageSource> LoadFromUrlAsync(string imageUrl, Task<ImageSource> defaultImage)
            => _downloadedImageCache.GetOrAdd(imageUrl, n => Schedule(() => LoadOfficialAsync(n, defaultImage)));

        private async Task<ImageSource> LoadOfficialAsync(string imageUrl, Task<ImageSource> defaultImage)
        {
            // Remove the query part, remove the host part, remove image/Art/2DItems/, trim slashes
            var relevantPart = Regex.Replace(imageUrl, @"(\?.*)|(.*(\.net|\.com)/)|(image/Art/2DItems/)", "").Trim('/');
            var match = Regex.Match(relevantPart, @"gen/image/.*?/([a-zA-Z0-9]*)/Item\.png");
            var isRelic = imageUrl.Contains("&relic=1");
            if (match.Success)
            {
                // These names are too long.
                // They contain groups of 20 chars (as folders) and end with a unique identifier.
                relevantPart = $"gen/{match.Groups[1]}.png";
            }
            else if (isRelic)
            {
                // Non-generated images for relics and normal uniques have the same url, the relic flag is in the
                // query part. As the query is removed, the file name needs to be adjusted for relics.
                relevantPart = relevantPart.Replace(".png", "Relic.png");
            }
            var fileName = string.Format(DownloadedPathFormat, relevantPart);

            if (!File.Exists(fileName))
            {
                if (!_options.DownloadMissingItemImages)
                    return await defaultImage.ConfigureAwait(false);

                var imgData = await _httpClient.GetByteArrayAsync(imageUrl).ConfigureAwait(false);
                CreateDirectories(fileName);
                WikiApiUtils.SaveImage(imgData, fileName, false);
                Log.Info($"Downloaded item image {fileName} to the file system.");
            }
            return BitmapImageFactory.Create(fileName);
        }

        private Task<T> Schedule<T>(Func<Task<T>> function)
            => _scheduler.ScheduleAsync(function);

        private static void CreateDirectories(string fileName)
        {
            var f = new FileInfo(fileName);
            if (f.DirectoryName != null)
            {
                Directory.CreateDirectory(f.DirectoryName);
            }
        }
    }
}