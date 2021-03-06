﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Abp.Modules;
using Abp.Web;
using Abp.Zero;

namespace NorthLion.Zero.Localization.Helpers
{
    public static class XmlLocations
    {

        private const string XmlLocationModuleZero = "NorthLion.Zero.Localization.Source";
        private const string XmlAbp = "Abp.Localization.Sources.AbpXmlSource";
        private const string XmlAbpZero = "Abp.Zero.Localization.Source";
        private const string XmlAbpWeb = "Abp.Web.Common.Web.Localization.AbpWeb";
        public static LangLocalization GetXmlLocationBySourceName(string source)
        {
            switch (source)
            {
                case "Zero":
                    return new LangLocalization() { Assembly = typeof(ZeroCoreModule), LocalizationNameSpace = XmlLocationModuleZero };
                case "Abp":
                    return new LangLocalization() { Assembly = typeof(AbpModule), LocalizationNameSpace = XmlAbp };
                case "AbpZero":
                    return new LangLocalization() { Assembly = typeof(AbpZeroCoreModule), LocalizationNameSpace = XmlAbpZero };
                //case "AbpWeb":
                //    return new LangLocalization() { Assembly = typeof(Abp), LocalizationNameSpace = XmlAbpWeb };
                default:
                    throw new ArgumentOutOfRangeException(nameof(source));
            }
        }

        public class LangLocalization
        {
            public string LocalizationNameSpace { get; set; }
            public Type Assembly { get; set; }
        }
    }
}
