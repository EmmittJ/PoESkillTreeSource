using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using POESKillTree.Localization;

namespace POESKillTree.ViewModels
{
    public class PoEBuild
    {
        public string Name { get; set; }
        public string CharacterName { get; set; }
        public string AccountName { get; set;}
        public string League { get; set; }
        public string Level { get; set; }
        public string Class { get; set; }
        public string PointsUsed { get; set; }
        public string Url { get; set; }
        public string Note { get; set; }
        public string ItemData { get; set; }
        public DateTime LastUpdated { get; set; }
        public List<string[]> CustomGroups { get; set; }
        public bool CurrentlyOpen { get; set; }

        [XmlIgnoreAttribute]
        public string Image
        {
            get
            {
                var imgPath = "/POESKillTree;component/Images/" +  Class;
                if (CurrentlyOpen)
                    imgPath += "_Highlighted";
                return imgPath + ".jpg";
            }
        }
        [XmlIgnoreAttribute]
        public string Description
        {
            get
            {
                uint used = 0;
                if (!string.IsNullOrEmpty(PointsUsed)) uint.TryParse(PointsUsed, out used);

                return string.Format(L10n.Plural("{0}, {1} point used", "{0}, {1} points used", used), Class, used);
            }
        }
        [XmlIgnoreAttribute]
        public bool Visible { get; set; }

        public PoEBuild()
        {
            Visible = true;
            CustomGroups = new List<string[]>();
        }

        public PoEBuild(string name, string poeClass, string pointsUsed, string url, string note)
        {
            Name = name;
            Class = poeClass;
            PointsUsed = pointsUsed;
            Url = url;
            Note = note;
            CustomGroups = new List<string[]>();
        }

        public override string ToString()
        {
            return Name + '\n' + Description;
        }

        public static PoEBuild Copy(PoEBuild build)
        {
            return new PoEBuild
            {
                Name = build.Name,
                CharacterName = build.CharacterName,
                AccountName = build.AccountName,
                League = build.League,
                Level = build.Level,
                Class = build.Class,
                PointsUsed = build.PointsUsed,
                Url = build.Url,
                Note = build.Note,
                ItemData = build.ItemData,
                LastUpdated = build.LastUpdated,
                CustomGroups = new List<string[]>(build.CustomGroups),
                CurrentlyOpen = build.CurrentlyOpen
            };
        }
    }
}