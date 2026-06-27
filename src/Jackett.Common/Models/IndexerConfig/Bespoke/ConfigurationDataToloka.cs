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
        public BoolConfigurationItem FetchGrabs { get; private set; }
        public BoolConfigurationItem NormalizeQuality { get; private set; }
        public SingleSelectConfigurationItem MaxPages { get; private set; }
        public DisplayInfoConfigurationItem PerformanceInfo { get; private set; }

        public ConfigurationDataToloka()
        {
            FreeleechOnly = new BoolConfigurationItem("Show freeleech only") { Value = false };
            StripCyrillicLetters = new BoolConfigurationItem("Strip Cyrillic Letters") { Value = true };
            PreserveExactRanges = new BoolConfigurationItem("Show exact episode/season ranges for disjoint packs (e.g. E01-E02, E05-E12) instead of a single envelope range (E01-E12). More truthful for the user; Sonarr still treats it as the first-to-last span.") { Value = false };
            AppendReleaseGroup = new BoolConfigurationItem("Append uploader as release group (e.g. ...WEB-DL-FanVoxUA, improves Sonarr/Radarr matching)") { Value = true };
            UseMagnetLinks = new BoolConfigurationItem("Use magnet links (fetches the details page on download)") { Value = false };
            EnhancedMetadata = new BoolConfigurationItem("Fetch enhanced metadata from each release's details page (IMDb id, poster, recovered resolution, file count) - slower: one extra rate-limited request per result, capped at the first 10") { Value = false };
            FetchGrabs = new BoolConfigurationItem("Fetch download counts (grabs) via one extra api.php request per search - Toloka's HTML search page hides the completed count (the api covers the first ~30 results of a search)") { Value = true };
            NormalizeQuality = new BoolConfigurationItem("Normalize source/quality names to the tokens Sonarr/Radarr parse (e.g. BDRemux -> BluRay Remux, BDRip -> BluRay). Disable to keep Toloka's original quality tokens.") { Value = true };
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
