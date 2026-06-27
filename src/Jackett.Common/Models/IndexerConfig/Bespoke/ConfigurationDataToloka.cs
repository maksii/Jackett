using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Jackett.Common.Models.IndexerConfig.Bespoke
{
    [ExcludeFromCodeCoverage]
    internal class ConfigurationDataToloka : ConfigurationDataBasicLogin
    {
        public BoolConfigurationItem FreeleechOnly { get; private set; }
        public BoolConfigurationItem StripCyrillicLetters { get; private set; }
        public BoolConfigurationItem PreserveExactRanges { get; private set; }
        public BoolConfigurationItem AppendReleaseGroup { get; private set; }
        public BoolConfigurationItem UseMagnetLinks { get; private set; }
        public BoolConfigurationItem EnhancedMetadata { get; private set; }
        public StringConfigurationItem SearchByUploader { get; private set; }
        public SingleSelectConfigurationItem MaxPages { get; private set; }
        public DisplayInfoConfigurationItem PerformanceInfo { get; private set; }

        public ConfigurationDataToloka()
        {
            FreeleechOnly = new BoolConfigurationItem("Show freeleech only") { Value = false };
            StripCyrillicLetters = new BoolConfigurationItem("Strip Cyrillic Letters") { Value = true };
            PreserveExactRanges = new BoolConfigurationItem("Show exact episode/season ranges for disjoint packs (e.g. E01-E02, E05-E12) instead of a single envelope range (E01-E12). More truthful for the user; Sonarr still treats it as the first-to-last span.") { Value = false };
            AppendReleaseGroup = new BoolConfigurationItem("Append uploader as release group (e.g. ...WEB-DL-FanVoxUA, improves Sonarr/Radarr matching)") { Value = true };
            UseMagnetLinks = new BoolConfigurationItem("Use magnet links (fetches the details page on download)") { Value = false };
            EnhancedMetadata = new BoolConfigurationItem("Fetch enhanced metadata (IMDb, poster, exact size) - slower, one extra request per result") { Value = false };
            SearchByUploader = new StringConfigurationItem("Search only releases from this uploader (optional). Enter the uploader's username (e.g. fanat22012); a numeric uploader id (e.g. 889220) is also accepted.") { Value = "" };
            MaxPages = new SingleSelectConfigurationItem(
                "Maximum number of result pages to fetch",
                new Dictionary<string, string>
                {
                    { "1", "1 page" },
                    { "2", "2 pages" },
                    { "3", "3 pages" },
                    { "4", "4 pages" },
                    { "5", "5 pages" }
                })
            { Value = "1" };
            PerformanceInfo = new DisplayInfoConfigurationItem(
                "Performance",
                "Enhanced metadata and additional result pages issue extra requests to Toloka and make searches slower. " +
                "Leave them at their defaults for the fastest searches.");
        }
    }
}
