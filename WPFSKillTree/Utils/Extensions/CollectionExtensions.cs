﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace POESKillTree.Utils.Extensions
{
    /// <summary>
    /// Extension methods for collections.
    /// </summary>
    public static class CollectionExtensions
    {
        /// <summary>
        /// Adds the given value to the collection stored in the given dictionary at the given key.
        /// Creates a new collection if there is none stored.
        /// </summary>
        /// <typeparam name="TKey">Dictionary key type</typeparam>
        /// <typeparam name="TValue">Type of values stored in the collections in the dictionary</typeparam>
        /// <typeparam name="TCollection">Type of collection stored in the dictonary</typeparam>
        /// <param name="dict">The dictionary to manipulate</param>
        /// <param name="key">The key which stored collection is to be accessed</param>
        /// <param name="value">The value which should be added to the collection in the dictonary belonging to the key</param>
        public static void Add<TKey, TValue, TCollection>(this Dictionary<TKey, TCollection> dict, TKey key, TValue value)
            where TCollection : ICollection<TValue>, new()
        {
            if (!dict.ContainsKey(key))
            {
                dict.Add(key, new TCollection());
            }
            dict[key].Add(value);
        }

        /// <summary>
        /// Executes the given action on every element in the enumerable.
        /// </summary>
        public static void ForEach<T>(this IEnumerable<T> enumerable, Action<T> action)
        {
            foreach (var item in enumerable)
            {
                action(item);
            }
        }

        public static IEnumerable<T> Flatten<T>(this IEnumerable<IEnumerable<T>> enumerable)
        {
            return enumerable.SelectMany(e => e);
        }

        public static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> toAdd)
        {
            toAdd.ForEach(collection.Add);
        }

        public static TValue GetOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dict, TKey key,
            Func<TValue> defaultValueProvider)
        {
            TValue value;
            return dict.TryGetValue(key, out value) ? value : defaultValueProvider();
        }

        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> source)
        {
            return new HashSet<T>(source);
        }

        public static IEnumerable<T> OrEmptyIfNull<T>(this IEnumerable<T> source)
        {
            if (source == null) return Enumerable.Empty<T>();
            return source;
        }
    }
}