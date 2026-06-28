using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Jackett.Common.Models.IndexerConfig.Bespoke
{
    [ExcludeFromCodeCoverage]
    internal class ConfigurationDataToloka : ConfigurationDataBasicLogin
    {
        public BoolConfigurationItem FreeleechOnly { get; private set; }
        public BoolConfigurationItem StripCyrillicLetters { get; private set; }
        public DisplayInfoConfigurationItem ReleaseTitleInfo { get; private set; }
        public BoolConfigurationItem AppendReleaseGroup { get; private set; }
        public BoolConfigurationItem NormalizeQuality { get; private set; }
        public BoolConfigurationItem PreserveExactRanges { get; private set; }
        public DisplayInfoConfigurationItem PerformanceInfo { get; private set; }
        public BoolConfigurationItem UseMagnetLinks { get; private set; }
        public BoolConfigurationItem EnhancedMetadata { get; private set; }
        public BoolConfigurationItem FetchGrabs { get; private set; }
        public SingleSelectConfigurationItem MaxPages { get; private set; }

        public ConfigurationDataToloka()
        {
            FreeleechOnly = new BoolConfigurationItem("Show freeleech only") { Value = false };
            StripCyrillicLetters = new BoolConfigurationItem("Strip Cyrillic Letters") { Value = true };

            ReleaseTitleInfo = new DisplayInfoConfigurationItem(
                "Release title formatting",
                "These options shape the release title so Sonarr and Radarr can parse and match it:" +
                "<ul>" +
                "<li><b>Append release group</b> &mdash; appends the uploader as a release group (e.g. ...WEB-DL-FanVoxUA) to improve Sonarr/Radarr matching.</li>" +
                "<li><b>Normalize quality names</b> &mdash; maps Toloka's source tokens to the ones Sonarr/Radarr parse (e.g. BDRemux &rarr; BluRay Remux, BDRip &rarr; BluRay). Disable to keep Toloka's original tokens.</li>" +
                "<li><b>Show exact episode/season ranges</b> &mdash; for disjoint packs, shows the real ranges (e.g. E01-E02, E05-E12) instead of a single envelope range (E01-E12). More truthful; Sonarr still treats it as the first-to-last span.</li>" +
                "</ul>");
            AppendReleaseGroup = new BoolConfigurationItem("Append release group") { Value = true };
            NormalizeQuality = new BoolConfigurationItem("Normalize quality names") { Value = true };
            PreserveExactRanges = new BoolConfigurationItem("Show exact episode/season ranges") { Value = false };

            PerformanceInfo = new DisplayInfoConfigurationItem(
                "Performance",
                "These options fetch extra data with additional requests to Toloka, which makes searches slower &mdash; disable the ones you don't need for the fastest searches:" +
                "<ul>" +
                "<li><b>Use magnet links</b> &mdash; resolves a magnet link from each release's details page when you download it (extra request at download time; falls back to the .torrent file).</li>" +
                "<li><b>Fetch enhanced metadata</b> &mdash; adds IMDb id, poster, recovered resolution and file count from each details page. One extra rate-limited request per result, capped at the first 10.</li>" +
                "<li><b>Fetch download counts</b> &mdash; fills in the grabs (completed) count via one extra api.php request per search; Toloka's HTML search page hides it (covers about the first 30 results).</li>" +
                "<li><b>Maximum number of result pages</b> &mdash; more pages return more results but issue more requests.</li>" +
                "</ul>");
            UseMagnetLinks = new BoolConfigurationItem("Use magnet links") { Value = false };
            EnhancedMetadata = new BoolConfigurationItem("Fetch enhanced metadata") { Value = false };
            FetchGrabs = new BoolConfigurationItem("Fetch download counts") { Value = true };
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
        }
    }
}
