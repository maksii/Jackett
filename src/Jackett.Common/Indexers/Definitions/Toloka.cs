using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp.Html.Parser;
using Jackett.Common.Models;
using Jackett.Common.Models.IndexerConfig.Bespoke;
using Jackett.Common.Services.Interfaces;
using Jackett.Common.Utils;
using Newtonsoft.Json.Linq;
using NLog;
using WebClient = Jackett.Common.Utils.Clients.WebClient;

namespace Jackett.Common.Indexers.Definitions
{
    [ExcludeFromCodeCoverage]
    public class Toloka : IndexerBase
    {
        public override string Id => "toloka";
        public override string Name => "Toloka.to";
        public override string Description => "Toloka is a UKRAINIAN Semi-Private site with a thriving file-sharing community";
        public override string SiteLink { get; protected set; } = "https://toloka.to/";
        public override string Language => "uk-UA";
        public override string Type => "semi-private";

        public override TorznabCapabilities TorznabCaps => SetCapabilities();

        // Toloka returns up to 50 rows per page; we page by item offset to stay correct even if that ever changes.
        private const int ResultsPerPage = 50;

        // Hard cap on how many releases we enrich with a details-page request, to keep searches responsive.
        private const int MaxEnhancedMetadataRequests = 30;

        private new ConfigurationDataToloka configData
        {
            get => (ConfigurationDataToloka)base.configData;
            set => base.configData = value;
        }

        private readonly TitleParser _titleParser = new TitleParser();

        // Forums under the "Українські субтитри" (Ukrainian subtitles) tree: original audio + Ukrainian subs.
        // Used only as a fallback when a release title carries no explicit audio-language token.
        private static readonly HashSet<string> _subtitlesOnlyForums = new HashSet<string>
        {
            "190", "70", "192", "193", "195", "194", "196", "197"
        };

        private string LoginUrl => SiteLink + "login.php";
        private string SearchUrl => SiteLink + "tracker.php";

        public Toloka(IIndexerConfigurationService configService, WebClient wc, Logger l, IProtectionService ps, ICacheService cs)
            : base(configService: configService,
                   client: wc,
                   logger: l,
                   p: ps,
                   cacheService: cs,
                   configData: new ConfigurationDataToloka())
        {
        }

        private TorznabCapabilities SetCapabilities()
        {
            var caps = new TorznabCapabilities
            {
                TvSearchParams = new List<TvSearchParam>
                {
                    TvSearchParam.Q, TvSearchParam.Season, TvSearchParam.Ep
                },
                MovieSearchParams = new List<MovieSearchParam>
                {
                    MovieSearchParam.Q
                },
                MusicSearchParams = new List<MusicSearchParam>
                {
                    MusicSearchParam.Q
                },
                BookSearchParams = new List<BookSearchParam>
                {
                    BookSearchParam.Q
                }
            };
            // movies
            caps.Categories.AddCategoryMapping(117, TorznabCatType.Movies, "Українське кіно");
            caps.Categories.AddCategoryMapping(84, TorznabCatType.Movies, "|-Мультфільми і казки");
            caps.Categories.AddCategoryMapping(42, TorznabCatType.Movies, "|-Художні фільми");
            caps.Categories.AddCategoryMapping(124, TorznabCatType.TV, "|-Телесеріали");
            caps.Categories.AddCategoryMapping(125, TorznabCatType.TV, "|-Мультсеріали");
            caps.Categories.AddCategoryMapping(129, TorznabCatType.Movies, "|-АртХаус");
            caps.Categories.AddCategoryMapping(219, TorznabCatType.Movies, "|-Аматорське відео");
            caps.Categories.AddCategoryMapping(118, TorznabCatType.Movies, "Українське озвучення");
            caps.Categories.AddCategoryMapping(16, TorznabCatType.Movies, "|-Фільми");
            caps.Categories.AddCategoryMapping(32, TorznabCatType.TV, "|-Телесеріали");
            caps.Categories.AddCategoryMapping(19, TorznabCatType.Movies, "|-Мультфільми");
            caps.Categories.AddCategoryMapping(44, TorznabCatType.TV, "|-Мультсеріали");
            caps.Categories.AddCategoryMapping(127, TorznabCatType.TVAnime, "|-Аніме");
            caps.Categories.AddCategoryMapping(55, TorznabCatType.Movies, "|-АртХаус");
            caps.Categories.AddCategoryMapping(94, TorznabCatType.MoviesOther, "|-Трейлери");
            caps.Categories.AddCategoryMapping(144, TorznabCatType.Movies, "|-Короткометражні");
            caps.Categories.AddCategoryMapping(190, TorznabCatType.Movies, "Українські субтитри");
            caps.Categories.AddCategoryMapping(70, TorznabCatType.Movies, "|-Фільми");
            caps.Categories.AddCategoryMapping(192, TorznabCatType.TV, "|-Телесеріали");
            caps.Categories.AddCategoryMapping(193, TorznabCatType.Movies, "|-Мультфільми");
            caps.Categories.AddCategoryMapping(195, TorznabCatType.TV, "|-Мультсеріали");
            caps.Categories.AddCategoryMapping(194, TorznabCatType.TVAnime, "|-Аніме");
            caps.Categories.AddCategoryMapping(196, TorznabCatType.Movies, "|-АртХаус");
            caps.Categories.AddCategoryMapping(197, TorznabCatType.Movies, "|-Короткометражні");
            caps.Categories.AddCategoryMapping(225, TorznabCatType.TVDocumentary, "Документальні фільми українською");
            caps.Categories.AddCategoryMapping(21, TorznabCatType.TVDocumentary, "|-Українські наукові документальні фільми");
            caps.Categories.AddCategoryMapping(131, TorznabCatType.TVDocumentary, "|-Українські історичні документальні фільми");
            caps.Categories.AddCategoryMapping(226, TorznabCatType.TVDocumentary, "|-BBC");
            caps.Categories.AddCategoryMapping(227, TorznabCatType.TVDocumentary, "|-Discovery");
            caps.Categories.AddCategoryMapping(228, TorznabCatType.TVDocumentary, "|-National Geographic");
            caps.Categories.AddCategoryMapping(229, TorznabCatType.TVDocumentary, "|-History Channel");
            caps.Categories.AddCategoryMapping(230, TorznabCatType.TVDocumentary, "|-Інші іноземні документальні фільми");
            caps.Categories.AddCategoryMapping(119, TorznabCatType.TVOther, "Телепередачі українською");
            caps.Categories.AddCategoryMapping(18, TorznabCatType.TVOther, "|-Музичне відео");
            caps.Categories.AddCategoryMapping(132, TorznabCatType.TVOther, "|-Телевізійні шоу та програми");
            caps.Categories.AddCategoryMapping(157, TorznabCatType.TVSport, "Український спорт");
            caps.Categories.AddCategoryMapping(235, TorznabCatType.TVSport, "|-Олімпіада");
            caps.Categories.AddCategoryMapping(170, TorznabCatType.TVSport, "|-Чемпіонати Європи з футболу");
            caps.Categories.AddCategoryMapping(162, TorznabCatType.TVSport, "|-Чемпіонати світу з футболу");
            caps.Categories.AddCategoryMapping(166, TorznabCatType.TVSport, "|-Чемпіонат та Кубок України з футболу");
            caps.Categories.AddCategoryMapping(167, TorznabCatType.TVSport, "|-Єврокубки");
            caps.Categories.AddCategoryMapping(168, TorznabCatType.TVSport, "|-Збірна України");
            caps.Categories.AddCategoryMapping(169, TorznabCatType.TVSport, "|-Закордонні чемпіонати");
            caps.Categories.AddCategoryMapping(54, TorznabCatType.TVSport, "|-Футбольне відео");
            caps.Categories.AddCategoryMapping(158, TorznabCatType.TVSport, "|-Баскетбол, хоккей, волейбол, гандбол, футзал");
            caps.Categories.AddCategoryMapping(159, TorznabCatType.TVSport, "|-Бокс, реслінг, бойові мистецтва");
            caps.Categories.AddCategoryMapping(160, TorznabCatType.TVSport, "|-Авто, мото");
            caps.Categories.AddCategoryMapping(161, TorznabCatType.TVSport, "|-Інший спорт, активний відпочинок");
            caps.Categories.AddCategoryMapping(136, TorznabCatType.MoviesHD, "HD українською");
            caps.Categories.AddCategoryMapping(96, TorznabCatType.MoviesHD, "|-Фільми в HD");
            caps.Categories.AddCategoryMapping(173, TorznabCatType.TVHD, "|-Серіали в HD");
            caps.Categories.AddCategoryMapping(139, TorznabCatType.MoviesHD, "|-Мультфільми в HD");
            caps.Categories.AddCategoryMapping(174, TorznabCatType.TVHD, "|-Мультсеріали в HD");
            caps.Categories.AddCategoryMapping(140, TorznabCatType.TVDocumentary, "|-Документальні фільми в HD");
            caps.Categories.AddCategoryMapping(120, TorznabCatType.MoviesDVD, "DVD українською");
            caps.Categories.AddCategoryMapping(66, TorznabCatType.MoviesDVD, "|-Художні фільми та серіали в DVD");
            caps.Categories.AddCategoryMapping(137, TorznabCatType.MoviesDVD, "|-Мультфільми та мультсеріали в DVD");
            caps.Categories.AddCategoryMapping(137, TorznabCatType.TV, "|-Мультфільми та мультсеріали в DVD");
            caps.Categories.AddCategoryMapping(138, TorznabCatType.MoviesDVD, "|-Документальні фільми в DVD");
            caps.Categories.AddCategoryMapping(237, TorznabCatType.Movies, "Відео для мобільних (iOS, Android, Windows Phone)");
            caps.Categories.AddCategoryMapping(33, TorznabCatType.AudioVideo, "Звукові доріжки та субтитри");
            // music
            caps.Categories.AddCategoryMapping(8, TorznabCatType.Audio, "Українська музика (lossy)");
            caps.Categories.AddCategoryMapping(23, TorznabCatType.Audio, "|-Поп, Естрада");
            caps.Categories.AddCategoryMapping(24, TorznabCatType.Audio, "|-Джаз, Блюз");
            caps.Categories.AddCategoryMapping(43, TorznabCatType.Audio, "|-Етно, Фольклор, Народна, Бардівська");
            caps.Categories.AddCategoryMapping(35, TorznabCatType.Audio, "|-Інструментальна, Класична та неокласична");
            caps.Categories.AddCategoryMapping(37, TorznabCatType.Audio, "|-Рок, Метал, Альтернатива, Панк, СКА");
            caps.Categories.AddCategoryMapping(36, TorznabCatType.Audio, "|-Реп, Хіп-хоп, РнБ");
            caps.Categories.AddCategoryMapping(38, TorznabCatType.Audio, "|-Електронна музика");
            caps.Categories.AddCategoryMapping(56, TorznabCatType.Audio, "|-Невидане");
            caps.Categories.AddCategoryMapping(98, TorznabCatType.AudioLossless, "Українська музика (lossless)");
            caps.Categories.AddCategoryMapping(100, TorznabCatType.AudioLossless, "|-Поп, Естрада");
            caps.Categories.AddCategoryMapping(101, TorznabCatType.AudioLossless, "|-Джаз, Блюз");
            caps.Categories.AddCategoryMapping(102, TorznabCatType.AudioLossless, "|-Етно, Фольклор, Народна, Бардівська");
            caps.Categories.AddCategoryMapping(103, TorznabCatType.AudioLossless, "|-Інструментальна, Класична та неокласична");
            caps.Categories.AddCategoryMapping(104, TorznabCatType.AudioLossless, "|-Рок, Метал, Альтернатива, Панк, СКА");
            caps.Categories.AddCategoryMapping(105, TorznabCatType.AudioLossless, "|-Реп, Хіп-хоп, РнБ");
            caps.Categories.AddCategoryMapping(106, TorznabCatType.AudioLossless, "|-Електронна музика");
            // books
            caps.Categories.AddCategoryMapping(11, TorznabCatType.Books, "Друкована література");
            caps.Categories.AddCategoryMapping(134, TorznabCatType.Books, "|-Українська художня література (до 1991 р.)");
            caps.Categories.AddCategoryMapping(177, TorznabCatType.Books, "|-Українська художня література (після 1991 р.)");
            caps.Categories.AddCategoryMapping(178, TorznabCatType.Books, "|-Зарубіжна художня література");
            caps.Categories.AddCategoryMapping(179, TorznabCatType.Books, "|-Наукова література (гуманітарні дисципліни)");
            caps.Categories.AddCategoryMapping(180, TorznabCatType.Books, "|-Наукова література (природничі дисципліни)");
            caps.Categories.AddCategoryMapping(183, TorznabCatType.Books, "|-Навчальна та довідкова");
            caps.Categories.AddCategoryMapping(181, TorznabCatType.BooksMags, "|-Періодика");
            caps.Categories.AddCategoryMapping(182, TorznabCatType.Books, "|-Батькам та малятам");
            caps.Categories.AddCategoryMapping(184, TorznabCatType.BooksComics, "|-Графіка (комікси, манґа, BD та інше)");
            caps.Categories.AddCategoryMapping(185, TorznabCatType.AudioAudiobook, "Аудіокниги українською");
            caps.Categories.AddCategoryMapping(135, TorznabCatType.AudioAudiobook, "|-Українська художня література");
            caps.Categories.AddCategoryMapping(186, TorznabCatType.AudioAudiobook, "|-Зарубіжна художня література");
            caps.Categories.AddCategoryMapping(187, TorznabCatType.AudioAudiobook, "|-Історія, біографістика, спогади");
            caps.Categories.AddCategoryMapping(189, TorznabCatType.AudioAudiobook, "|-Сирий матеріал");
            // software
            caps.Categories.AddCategoryMapping(9, TorznabCatType.PC, "Windows");
            caps.Categories.AddCategoryMapping(25, TorznabCatType.PC, "|-Windows");
            caps.Categories.AddCategoryMapping(199, TorznabCatType.PC, "|-Офіс");
            caps.Categories.AddCategoryMapping(200, TorznabCatType.PC, "|-Антивіруси та безпека");
            caps.Categories.AddCategoryMapping(201, TorznabCatType.PC, "|-Мультимедія");
            caps.Categories.AddCategoryMapping(202, TorznabCatType.PC, "|-Утиліти, обслуговування, мережа");
            caps.Categories.AddCategoryMapping(239, TorznabCatType.PC, "Linux, Mac OS");
            caps.Categories.AddCategoryMapping(26, TorznabCatType.PC, "|-Linux");
            caps.Categories.AddCategoryMapping(27, TorznabCatType.PCMac, "|-Mac OS");
            caps.Categories.AddCategoryMapping(240, TorznabCatType.PC, "Інші OS");
            caps.Categories.AddCategoryMapping(211, TorznabCatType.PCMobileAndroid, "|-Android");
            caps.Categories.AddCategoryMapping(122, TorznabCatType.PCMobileiOS, "|-iOS");
            caps.Categories.AddCategoryMapping(40, TorznabCatType.PCMobileOther, "|-Інші мобільні платформи");
            caps.Categories.AddCategoryMapping(241, TorznabCatType.Other, "Інше");
            caps.Categories.AddCategoryMapping(203, TorznabCatType.Other, "|-Інфодиски, електронні підручники, відеоуроки");
            caps.Categories.AddCategoryMapping(12, TorznabCatType.Other, "|-Шпалери, фотографії та зображення");
            caps.Categories.AddCategoryMapping(249, TorznabCatType.Other, "|-Веб-скрипти");
            // games
            caps.Categories.AddCategoryMapping(10, TorznabCatType.PCGames, "Ігри українською");
            caps.Categories.AddCategoryMapping(28, TorznabCatType.PCGames, "|-PC ігри");
            caps.Categories.AddCategoryMapping(259, TorznabCatType.PCGames, "|-Mac ігри");
            caps.Categories.AddCategoryMapping(29, TorznabCatType.PCGames, "|-Українізації, доповнення, патчі...");
            caps.Categories.AddCategoryMapping(30, TorznabCatType.PCGames, "|-Мобільні та консольні ігри");
            caps.Categories.AddCategoryMapping(41, TorznabCatType.PCMobileiOS, "|-iOS");
            caps.Categories.AddCategoryMapping(212, TorznabCatType.PCMobileAndroid, "|-Android");
            caps.Categories.AddCategoryMapping(205, TorznabCatType.PCGames, "Переклад ігор українською");
            // Role-gated section: only visible to users with elevated forum permissions, so most accounts get nothing here.
            caps.Categories.AddCategoryMapping(236, TorznabCatType.Other, "Закритий розділ");
            // Archive: still-valid releases relocated here, usually because a newer version superseded them elsewhere.
            caps.Categories.AddCategoryMapping(71, TorznabCatType.Other, "Архіви");
            caps.Categories.AddCategoryMapping(72, TorznabCatType.Other, "Архів відео");
            caps.Categories.AddCategoryMapping(73, TorznabCatType.Other, "Архів музики");
            caps.Categories.AddCategoryMapping(74, TorznabCatType.Other, "Архів програм");
            caps.Categories.AddCategoryMapping(75, TorznabCatType.Other, "Архів ігор");
            caps.Categories.AddCategoryMapping(76, TorznabCatType.Other, "Архів літератури");
            // Unformatted: flagged for a description/formatting violation (often just a missing poster); the file itself may be fine.
            caps.Categories.AddCategoryMapping(121, TorznabCatType.Other, "Неоформлені");
            caps.Categories.AddCategoryMapping(45, TorznabCatType.Other, "Неоформлене відео");
            caps.Categories.AddCategoryMapping(46, TorznabCatType.Other, "Неоформлена музика");
            caps.Categories.AddCategoryMapping(47, TorznabCatType.Other, "Неоформлене програмне забезпечення");
            caps.Categories.AddCategoryMapping(48, TorznabCatType.Other, "Неоформлені ігри");
            caps.Categories.AddCategoryMapping(208, TorznabCatType.Other, "Неоформлена література");

            return caps;
        }

        public override async Task<IndexerConfigurationStatus> ApplyConfiguration(JToken configJson)
        {
            LoadValuesFromJson(configJson);

            var pairs = new Dictionary<string, string>
            {
                { "username", configData.Username.Value },
                { "password", configData.Password.Value },
                { "autologin", "on" },
                { "ssl", "on" },
                { "redirect", "" },
                { "login", "Вхід" }
            };

            var result = await RequestLoginAndFollowRedirect(LoginUrl, pairs, CookieHeader, true, null, LoginUrl, true);
            await ConfigureIfOK(result.Cookies, result.ContentString != null && result.ContentString.Contains("logout=true"), () =>
            {
                var loginResultParser = new HtmlParser();
                using var loginResultDocument = loginResultParser.ParseDocument(result.ContentString);
                var errorMessage = loginResultDocument.QuerySelector("table.forumline table span.gen")?.FirstChild?.TextContent.Trim();

                throw new ExceptionWithConfigData(errorMessage ?? "Login failed, please check your username and password.", configData);
            });

            return IndexerConfigurationStatus.RequiresTesting;
        }

        protected override string ResolveCookies(string incomingCookies = "")
        {
            var cookieDictionary = CookieUtil.CookieHeaderToDictionary(base.ResolveCookies(incomingCookies));

            var badCookies = cookieDictionary.Where(x => x.Key.StartsWith("toloka_") && x.Key.EndsWith("_u")).ToList();
            badCookies.ForEach(x => cookieDictionary.Remove(x.Key));

            return CookieUtil.CookieDictionaryToHeader(cookieDictionary);
        }

        protected override async Task<IEnumerable<ReleaseInfo>> PerformQuery(TorznabQuery query)
        {
            var releases = new List<ReleaseInfo>();
            var seenDetails = new HashSet<string>();

            var categories = MapTorznabCapsToTrackers(query);

            var maxPages = ParseUtil.CoerceInt(configData.MaxPages.Value);
            if (maxPages < 1)
            {
                maxPages = 1;
            }

            // The step/stop threshold is the real page size, learned from the first page rather than assumed,
            // so pagination keeps working if the site ever serves a different number of rows per page.
            var pageSize = ResultsPerPage;

            for (var page = 0; page < maxPages; page++)
            {
                var searchUrl = BuildSearchUrl(query, categories, page * pageSize);
                var results = await RequestWithCookiesAsync(searchUrl);

                // Session expired - re-login once and retry the same page. Re-checked every page in case the
                // session lapses mid-pagination.
                if (results.ContentString == null || !results.ContentString.Contains("logout=true"))
                {
                    await ApplyConfiguration(null);
                    results = await RequestWithCookiesAsync(searchUrl);
                }

                int rowsOnPage;
                try
                {
                    rowsOnPage = ParsePage(results.ContentString, releases, seenDetails);
                }
                catch (Exception ex)
                {
                    OnParseError(results.ContentString, ex);
                    break;
                }

                // If the first page is larger than our assumed size, adopt the real page size so subsequent
                // offsets step correctly. A smaller first page just means a single (last) page - stop below.
                if (page == 0 && rowsOnPage > pageSize)
                {
                    pageSize = rowsOnPage;
                }

                // Stop paging when a page is empty or not full (last page reached).
                if (rowsOnPage == 0 || rowsOnPage < pageSize)
                {
                    break;
                }
            }

            if (configData.EnhancedMetadata.Value)
            {
                await EnrichReleasesAsync(releases);
            }

            return releases;
        }

        private string BuildSearchUrl(TorznabQuery query, ICollection<string> categories, int offset)
        {
            var searchString = query.SanitizedSearchTerm;

            var qc = new List<KeyValuePair<string, string>> // NameValueCollection don't support cat[]=19&cat[]=6
            {
                { "o", "1" },
                { "s", "2" }
            };

            if (configData.FreeleechOnly.Value)
            {
                qc.Add("sds", "1");
            }

            var uploader = configData.SearchByUploader.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(uploader))
            {
                // Toloka's "Автор" search field is "pn" (poster NAME = username, what users actually know). The
                // legacy numeric poster id is "pid" (the value behind an uploader-name link). A purely numeric value
                // is treated as a pid for backward compatibility; anything else is a username. Homoglyph-normalize
                // the username so a handle with Cyrillic look-alikes hidden among Latin letters (e.g. "wаrden", the
                // 'а' is Cyrillic) still matches the real account — same treatment titles get.
                if (uploader.All(char.IsDigit))
                {
                    qc.Add("pid", uploader);
                }
                else
                {
                    qc.Add("pn", TitleParser.NormalizeNameHomoglyphs(uploader));
                }
            }

            if (string.IsNullOrWhiteSpace(searchString))
            {
                // empty search term -> "new torrents" view
                qc.Add("nm", string.Empty);
            }
            else
            {
                searchString = searchString.Replace("-", " ");

                if (query.Season is > 0)
                {
                    searchString += " Сезон " + query.Season;
                }

                qc.Add("nm", searchString);
            }

            foreach (var cat in categories)
            {
                qc.Add("f[]", cat);
            }

            if (offset > 0)
            {
                qc.Add("start", offset.ToString());
            }

            return SearchUrl + "?" + qc.GetQueryString();
        }

        // Parses one search results page, appending new releases. Returns the number of rows seen on the page.
        private int ParsePage(string pageContent, List<ReleaseInfo> releases, HashSet<string> seenDetails)
        {
            var searchResultParser = new HtmlParser();
            using var searchResultDocument = searchResultParser.ParseDocument(pageContent);

            // The results live in a table.forumline; rows are tr.prow1/tr.prow2. Live pages ship without a <tbody>,
            // so we use a descendant selector (the search-form table above has no prow rows).
            var rows = searchResultDocument.QuerySelectorAll("table.forumline tr[class*=\"prow\"]");
            var count = 0;

            foreach (var row in rows)
            {
                count++;
                try
                {
                    var release = ParseRow(row);
                    if (release == null)
                    {
                        continue;
                    }

                    // Dedupe across pages (page size can exceed ResultsPerPage, causing overlap).
                    if (!seenDetails.Add(release.Details.AbsoluteUri))
                    {
                        continue;
                    }

                    releases.Add(release);
                }
                catch (Exception ex)
                {
                    logger.Error($"{Id}: Error while parsing row '{row.OuterHtml}':\n\n{ex}");
                }
            }

            return count;
        }

        private ReleaseInfo ParseRow(AngleSharp.Dom.IElement row)
        {
            var qDownloadLink = row.QuerySelector("td:nth-child(6) > a");
            if (qDownloadLink == null) // No download cell = release still awaiting moderation; skip it.
            {
                return null;
            }

            var qDetailsLink = row.QuerySelector("td:nth-child(3) > a");
            if (qDetailsLink == null)
            {
                return null;
            }

            var details = new Uri(SiteLink + qDetailsLink.GetAttribute("href"));
            var title = qDetailsLink.TextContent.Trim();
            var link = new Uri(SiteLink + qDownloadLink.GetAttribute("href"));

            var forumLink = row.QuerySelector("td:nth-child(2) > a")?.GetAttribute("href");
            var forumId = forumLink != null ? ParseUtil.GetArgumentFromQueryString(forumLink, "f") : null;
            var category = MapTrackerCatToNewznab(forumId);

            var uploader = row.QuerySelector("td:nth-child(4)")?.TextContent;
            var releaseGroup = configData.AppendReleaseGroup.Value ? TitleParser.SanitizeReleaseGroup(uploader) : null;

            // Forum language convention used only when a title carries no explicit audio token: the "Українські
            // субтитри" tree is original-audio + Ukrainian subs (no Ukr audio tag); every other video forum is a
            // Ukrainian dub/original tree. Non-video forums get no audio-language tagging.
            var isVideoForum = TitleParser.IsAnyVideoCategory(category);
            bool? ukrainianAudioDefault = isVideoForum
                ? !(forumId != null && _subtitlesOnlyForums.Contains(forumId))
                : (bool?)null;

            var seedersStr = row.QuerySelector("td:nth-child(10) > b")?.TextContent;
            var seeders = string.IsNullOrWhiteSpace(seedersStr) ? 0 : ParseUtil.CoerceInt(seedersStr);
            var leechers = ParseUtil.CoerceInt(row.QuerySelector("td:nth-child(11) > b")?.TextContent);

            var release = new ReleaseInfo
            {
                Guid = details,
                Details = details,
                Link = configData.UseMagnetLinks.Value ? details : link,
                Title = _titleParser.Parse(title, category, configData.StripCyrillicLetters.Value, releaseGroup, ukrainianAudioDefault, configData.PreserveExactRanges.Value),
                Description = title,
                Category = category,
                Size = ParseUtil.GetBytes(row.QuerySelector("td:nth-child(7)")?.TextContent),
                Seeders = seeders,
                Peers = leechers + seeders,
                Grabs = ParseUtil.CoerceLong(row.QuerySelector("td:nth-child(9)")?.TextContent ?? string.Empty),
                PublishDate = DateTimeUtil.FromFuzzyTime(row.QuerySelector("td:nth-child(13)")?.TextContent ?? string.Empty),
                DownloadVolumeFactor = 1,
                UploadVolumeFactor = 1,
                MinimumRatio = 1,
                MinimumSeedTime = 0
            };

            if (row.QuerySelector("img[src=\"images/gold.gif\"], img[src=\"images/authors.gif\"]") != null)
            {
                release.DownloadVolumeFactor = 0;
            }
            else if (row.QuerySelector("img[src=\"images/silver.gif\"]") != null)
            {
                release.DownloadVolumeFactor = 0.5;
            }
            else if (row.QuerySelector("img[src=\"images/bronze.gif\"]") != null)
            {
                release.DownloadVolumeFactor = 0.75;
            }

            return release;
        }

        // Optionally fetches each release's details page to populate IMDb id, poster and infohash. Capped and sequential
        // to avoid hammering the tracker. Only runs when the user opts in.
        private async Task EnrichReleasesAsync(List<ReleaseInfo> releases)
        {
            var enriched = 0;

            foreach (var release in releases)
            {
                if (enriched >= MaxEnhancedMetadataRequests)
                {
                    logger.Debug($"{Id}: Enhanced metadata limited to the first {MaxEnhancedMetadataRequests} results.");
                    break;
                }

                enriched++;

                try
                {
                    var detailsResponse = await RequestWithCookiesAsync(release.Details.AbsoluteUri + "?spmode=full");
                    var meta = ParseDetailsPage(detailsResponse.ContentString);

                    if (meta.Imdb != null)
                    {
                        release.Imdb = meta.Imdb;
                    }

                    if (meta.Poster != null)
                    {
                        release.Poster = meta.Poster;
                    }

                    if (!string.IsNullOrWhiteSpace(meta.InfoHash))
                    {
                        release.InfoHash = meta.InfoHash;
                    }

                    if (!string.IsNullOrWhiteSpace(meta.Resolution))
                    {
                        release.Title = InjectResolution(release.Title, meta.Resolution);
                    }

                    if (configData.UseMagnetLinks.Value && meta.MagnetUri != null)
                    {
                        release.MagnetUri = meta.MagnetUri;
                        release.Link = meta.MagnetUri;
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn($"{Id}: Failed to fetch enhanced metadata for {release.Details}: {ex.Message}");
                }
            }
        }

        public override async Task<byte[]> Download(Uri link)
        {
            // In magnet mode the release Link points at the details page; resolve the magnet on demand.
            if (configData.UseMagnetLinks.Value && link.Scheme != "magnet" && !link.OriginalString.Contains("download.php"))
            {
                var response = await RequestWithCookiesAsync(link + "?spmode=full");
                var meta = ParseDetailsPage(response.ContentString);

                if (meta.MagnetUri != null)
                {
                    return await base.Download(meta.MagnetUri);
                }

                // Most Toloka releases have no magnet - fall back to the .torrent download link.
                if (!string.IsNullOrWhiteSpace(meta.DownloadLink))
                {
                    return await base.Download(new Uri(SiteLink + meta.DownloadLink));
                }

                throw new Exception($"Failed to fetch magnet or torrent link from {link}");
            }

            return await base.Download(link);
        }

        public static DetailsMetadata ParseDetailsPage(string content)
        {
            var meta = new DetailsMetadata();

            if (string.IsNullOrWhiteSpace(content))
            {
                return meta;
            }

            var parser = new HtmlParser();
            using var doc = parser.ParseDocument(content);

            var magnet = doc.QuerySelector("a[href^=\"magnet:?\"]")?.GetAttribute("href");
            if (!string.IsNullOrWhiteSpace(magnet))
            {
                meta.MagnetUri = new Uri(magnet);
                meta.InfoHash = ParseInfoHashFromMagnet(magnet);
            }

            var imdbLink = doc.QuerySelector("a[href*=\"imdb.com/title/tt\"]")?.GetAttribute("href");
            if (!string.IsNullOrWhiteSpace(imdbLink))
            {
                var imdbMatch = Regex.Match(imdbLink, @"tt(\d{1,8})", RegexOptions.IgnoreCase);
                if (imdbMatch.Success)
                {
                    meta.Imdb = ParseUtil.GetImdbId(imdbMatch.Value);
                }
            }

            var poster = doc.QuerySelector("link[rel=\"image_src\"]")?.GetAttribute("href")
                         ?? doc.QuerySelector("[rel=\"image_src\"]")?.GetAttribute("href");
            if (!string.IsNullOrWhiteSpace(poster))
            {
                if (poster.StartsWith("//"))
                {
                    poster = "https:" + poster;
                }

                if (Uri.TryCreate(poster, UriKind.Absolute, out var posterUri))
                {
                    meta.Poster = posterUri;
                }
            }

            // .torrent download link on the details page (used as a fallback when no magnet is present).
            meta.DownloadLink = doc.QuerySelector("a[href*=\"download.php?id=\"]")?.GetAttribute("href");

            // Recover a resolution from the MediaInfo frame-size line ("розмір кадру: 1024 х 576").
            meta.Resolution = ResolutionFromFrameSize(doc.Body?.TextContent);

            return meta;
        }

        // Inserts a recovered resolution token into a title that has none, right after the "(Year)" anchor so it
        // sits with the rest of the quality block. No-op if the title already advertises a resolution.
        private static string InjectResolution(string title, string resolution)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(resolution))
            {
                return title;
            }

            if (Regex.IsMatch(title, @"\b(?:2160p|1080p|720p|576p|480p|4K)\b", RegexOptions.IgnoreCase))
            {
                return title;
            }

            var yearMatch = Regex.Match(title, @"\((?:19|20)\d{2}(?:\s*-\s*(?:19|20)\d{2})?\)");
            if (yearMatch.Success)
            {
                var insertAt = yearMatch.Index + yearMatch.Length;
                return title.Substring(0, insertAt) + " " + resolution + title.Substring(insertAt);
            }

            return title + " " + resolution;
        }

        private static string ParseInfoHashFromMagnet(string magnet)
        {
            var match = Regex.Match(magnet, @"urn:btih:([A-Fa-f0-9]{40}|[A-Za-z2-7]{32})", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        public sealed class DetailsMetadata
        {
            public Uri MagnetUri { get; set; }
            public string InfoHash { get; set; }
            public long? Imdb { get; set; }
            public Uri Poster { get; set; }
            public string DownloadLink { get; set; }
            public string Resolution { get; set; }
        }

        // Frame size reported in the details-page MediaInfo block, e.g. "розмір кадру: 1024 х 576" (Cyrillic х or
        // Latin x). Reliably present and used to recover a resolution token for the ~85% of titles that omit one.
        private static readonly Regex _frameSizeRegex = new Regex(@"(\d{3,4})\s*[хx]\s*(\d{3,4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Maps an actual frame size to the closest standard resolution token. Keyed on the larger dimension so
        // letterboxed/cropped widths (e.g. 1920x800, 1280x536) still map correctly. Conservative: never upgrades
        // beyond what the real pixels support, so it cannot introduce a false higher quality.
        private static string ResolutionFromFrameSize(string videoInfo)
        {
            if (string.IsNullOrWhiteSpace(videoInfo))
            {
                return null;
            }

            // Anchor on the Ukrainian "frame size" label so we read the real video dimensions and not some other
            // number (poster size, bitrate, etc.) elsewhere on the page.
            var label = videoInfo.IndexOf("кадру", StringComparison.OrdinalIgnoreCase);
            if (label < 0)
            {
                return null;
            }

            var m = _frameSizeRegex.Match(videoInfo, label);
            if (!m.Success)
            {
                return null;
            }

            var w = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var h = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);

            if (h >= 1400 || w >= 2560)
            {
                return "2160p";
            }

            if (h >= 900 || w >= 1800)
            {
                return "1080p";
            }

            if (h >= 700 || w >= 1260)
            {
                return "720p";
            }

            if (h >= 520 || w >= 1000)
            {
                return "576p";
            }

            return "480p";
        }

        public class TitleParser
        {
            private static readonly List<Regex> _FindTagsInTitlesRegexList = new List<Regex>
            {
                new Regex(@"\((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!))\)"),
                new Regex(@"\[(?>\[(?<c>)|[^\[\]]+|\](?<-c>))*(?(c)(?!))\]")
            };

            private readonly Regex _tvTitleCommaRegex = new Regex(@"\s(\d+),(\d+)", RegexOptions.Compiled);
            private readonly Regex _tvTitleCyrillicXRegex = new Regex(@"([\s-])Х+([\)\]])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // Season RANGE "Сезони 1-6" -> "S1-6". The lookbehind rejects a number-FIRST "2 сезон 1-18" (which is
            // "season 2, episodes 1-18", not a "season 1-18" range) so the digit before the keyword is not ignored.
            private readonly Regex _tvTitleMultipleSeasonsRegex = new Regex(@"(?<!\d\s{0,3})(?:Сезон\w*|Seasons?)\s*[:]*\s+(\d+-\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private readonly Regex _tvTitleSeasonNumberFirstRegex = new Regex(@"\b(\d{1,2})(?:nd|rd|th|st)?\s+(?:сезон\w*|seasons?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            private readonly Regex _tvTitleUkrSeasonEpisodeOfRegex = new Regex(@"Сезон\s*[:]*\s+(\d+)[^()]*?(?:Серії|Серія|Серій|Епізоди?)+\s*[:]*\s+(\d+(?:-\d+)?)\s*з\s*([\w?])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private readonly Regex _tvTitleUkrSeasonEpisodeRegex = new Regex(@"Сезон\s*[:]*\s+(\d+)[^()]*?(?:Серії|Серія|Серій|Епізоди?)+\s*[:]*\s+(\d+(?:-\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private readonly Regex _tvTitleUkrSeasonRegex = new Regex(@"Сезон\w*\s*[-:]?\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private readonly Regex _tvTitleUkrEpisodeOfRegex = new Regex(@"(?:Серії|Серія|Серій|Епізоди?)+\s*[:]*\s+(\d+(?:-\d+)?)\s*з\s*([\w?])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private readonly Regex _tvTitleUkrEpisodeRegex = new Regex(@"(?:Серії|Серія|Серій|Епізоди?)+\s*[:]*\s+(\d+(?:-\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            private readonly Regex _tvTitleEngSeasonEpisodeOfRegex = new Regex(@"Season\s*[:]*\s+(\d+)[^()]*?(?:Episodes?)+\s*[:]*\s+(\d+(?:-\d+)?)\s*of\s*([\w?])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private readonly Regex _tvTitleEngSeasonEpisodeRegex = new Regex(@"Season\s*[:]*\s+(\d+)[^()]*?(?:Episodes?)+\s*[:]*\s+(\d+(?:-\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private readonly Regex _tvTitleEngSeasonRegex = new Regex(@"Season\s*[:]*\s+(\d+(?:-\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private readonly Regex _tvTitleEngEpisodeOfRegex = new Regex(@"(?:Episodes?)+\s*[:]*\s+(\d+(?:-\d+)?)\s*of\s*([\w?])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private readonly Regex _tvTitleEngEpisodeRegex = new Regex(@"(?:Episodes?)+\s*[:]*\s+(\d+(?:-\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            private readonly Regex _stripCyrillicRegex = new Regex(@"(\([\p{IsCyrillic}\W]+\))|(^[\p{IsCyrillic}\W\d]+\/ )|([\p{IsCyrillic} \-]+,+)|([\p{IsCyrillic}]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            private static readonly Regex _codecAvcRegex = new Regex(@"\b(?:AVC|H\.?\s?264|x?264)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex _codecHevcRegex = new Regex(@"\b(?:HEVC|H\.?\s?265|x?265)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // Year (or year range/list) used to anchor the reconstruction of the release title. Also matches
            // comma/slash separated year lists for collection packs, e.g. "(1968,1970,2001)" or "(1990/2017)".
            // Year (or year range/list). Tolerates a trailing descriptor word before the close ("2005 ver",
            // "1995 року") so "(1991-1995 / 2005 ver)" still anchors.
            private static readonly Regex _yearAnchorRegex = new Regex(@"[\(\[]\s*(?:19|20)\d{2}(?:\s*[-,/]\s*(?:(?:19|20)\d{2}|\d{2}))*(?:\s+(?:ver\w*|version|рік\w*|року|год\w*))?\s*[\)\]]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // A bare (un-parenthesized) year (or year range "2006-2007") immediately followed by a source/resolution
            // token - a real year, not a title word ("Перші ластівки ... 2019 480p", "Феєрія ... 2006-2007 TVRip").
            private static readonly Regex _bareYearAnchorRegex = new Regex(
                @"(?<![\d./\-])(?:19|20)\d{2}(?:\s*-\s*(?:19|20)\d{2})?(?![\d./\-])(?=\s+(?:\d{3,4}[pi]|4K|2K|WEB|BD|BR|Blu-?Ray|HDDVD|HDTV|HDRip|HD|UHD|DVD|DVB|SAT|IPTV|TVRip|VHS|CAM|LD|DS|Site|PDTV|DCP|x26[45]|H\.?\s?26[45]|HEVC|AVC|XviD|DivX))",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // A full date OR a Cyrillic-month-year in parentheses/brackets. Group 1 = D.M.Y year, group 2 = Y.M.D
            // year, group 3 = "(Червень 2019)" / "(Червень 2019 (2))" month-name year. Reduced to just the year.
            private static readonly Regex _dateToYearRegex = new Regex(
                @"[\(\[]\s*(?:\d{1,2}[.\-/]\d{1,2}[.\-/]((?:19|20)\d{2})|((?:19|20)\d{2})[.\-/]\d{1,2}[.\-/]\d{1,2}|[А-Яа-яІіЇїЄєҐґ]+\s+((?:19|20)\d{2})(?:\s*\(\d+\))?)\s*[\)\]]",
                RegexOptions.Compiled);
            // Anchors a yearless release: the first resolution/source token marks where the title ends and the
            // quality block begins. Restricted to resolutions and unambiguous source tags - colour/format tags
            // (HDR, Hybrid, UHD), bare "Remux" and codecs are excluded because they can occur inside a real title.
            private static readonly Regex _sourceAnchorRegex = new Regex(@"\b(?:\d{3,4}[pi]|4K|2K|UHDTVRip|WEB-?DL|WB-?DLRip|WEB-?Rip|HD-?DVDRip|HDDVDRip|HDTVRip|HDTRip|HDTV|HDRip|BDRemux|BD\s?Rip|BRRip|Blu-?Ray|DVD\s?Remux|DVD\s?Upscale|\d+\s?x\s?DVD[59]|DVD[59]\s?\+\s?DVD[59]|DVDRip|DVD-?9|DVD-?5|VCDRip|DVB\s?Rip|IPTVRemux|SATRemux|DVBRemux|IPTVRip|PDTVRip|SiteRip|DSRip|DTVRip|DCPRip|LDR?Rip|VHSRip|SATRip|TVRip|CAMRip)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex _seasonEpisodeTokenRegex = new Regex(@"S\d{1,3}(?:-\d{1,3})?(?:E\d{1,3}(?:-E?\d{1,3})?)?|(?<![A-Za-z])E\d{1,3}(?:-\d{1,3})?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // Trailing pack/collection descriptors that break series/movie lookup if left in the title.
            private static readonly Regex _collectionDescriptorRegex = new Regex(
                @"\s*[.:]\s*(?:(?:The\s+)?Complete(?:\s+Collection|\s+Series)?|Collection|Dilogy|Trilogy|Tetralogy|Quadrilogy|Pentalogy|Hexalogy|Anthology|All\s+seasons(?:\s*\+\s*movie)?)\b.*$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // A "(Mini Series)" / "(міні серіал)" descriptor -> the single season S01. Replaced early in the isTv block.
            private static readonly Regex _miniSeriesRegex = new Regex(@"\(?\s*(?:Mini[\s-]?Series|міні[\s-]?серіал\w*)\s*\)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // An "except episodes N, M" clause ("окрім 2 та 13 серії") - the listed episodes are EXCLUDED, so their
            // numbers must not become the episode range; the clause is dropped entirely.
            private static readonly Regex _exceptClauseRegex = new Regex(@"\b(?:окрім|крім|except)\s+[\dтаіand,\s-]+\s*(?:сері[йіяї]+|епізод\w*|episodes?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // A DISC count/range ("Discs 1-16 of 16", "Диски 1-16 з 16", "DVDs 11 of 11") - disc numbers, never
            // episodes. Dropped wholesale so neither the range nor the trailing "of N" total becomes an episode token.
            private static readonly Regex _discCountRegex = new Regex(@"\b(?:Discs?|Диск[иівa-я]*|DVDs)\s*\d+(?:\s*-\s*\d+)?\s*(?:з|із|of)\s*\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // Alternatives are ordered longest/most-specific first so a prefix token never shadows a longer source
            // (e.g. "HDRip" must win over "HDR", "IPTVRip" over a bare source). New broadcast sources (SiteRip,
            // IPTVRip/Remux, PDTVRip, DSRip) are included so they survive HasLatinTitle and are preserved in output.
            private static readonly Regex _qualityTokenRegex = new Regex(@"\d{3,4}[pi]|4K|2K|UHDTVRip|WEB-?DL|WB-?DLRip|WEB-?Rip|HD-?DVDRip|HDDVDRip|HDTVRip|HDTRip|HDTV|HDRip|BDRemux|BD\s?Rip|BRRip|Blu-?Ray|DVD\s?Remux|DVD\s?Upscale|\d+\s?x\s?DVD[59]|DVD[59]\s?\+\s?DVD[59]|DVDRip|DVD-?9|DVD-?5|VCDRip|DVB\s?Rip|IPTVRemux|SATRemux|DVBRemux|IPTVRip|IPTV|PDTVRip|SiteRip|DSRip|DTVRip|DCPRip|LDR?Rip|VHSRip|SATRip|TVRip|CAMRip|Remux|UHD|HDR10\+|HDR10|HDR|HLG|SDR|DoVi|Dolby\s?Vision|\bDV\b|Hybrid|HDV|3D|DVD|x264|x265|XviD|DivX|AV1", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // Edition/version markers Sonarr/Radarr use for upgrade and edition matching. Dropping these silently
            // would lose real, valuable data, so they are extracted and re-appended to the rebuilt title.
            private static readonly Regex _editionTagRegex = new Regex(@"\b(?:PROPER|REPACK|RERIP|REMASTERED|REMASTER|Director'?s\s*Cut|Extended(?:\s*(?:Cut|Edition))?|Special\s*Edition|Collector'?s\s*Edition|Limited\s*Edition|Silent\s*Version|Open\s*Matte|(?:The\s+)?Complete\s+Restored(?:\s*Edition)?|Recut|Euro\s*Cut|European\s*Cut|AI[\s\-]?Upscale[d]?|Uncut|Unrated|Theatrical(?:\s*Cut)?|IMAX|AI[\s\-]?Rem(?:aster(?:ed)?)?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // "Part"/"Cour" markers (Ukrainian "Частина"/"Частини"). Without these, distinct parts collide on the same title.
            private static readonly Regex _partTagRegex = new Regex(@"\b(?:Частин[аиі]|Part[s]?|Cour)\s*[:]*\s*(\d{1,2}(?:\s*[-,]\s*\d{1,2})*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // OVA/ONA/OAD/Specials markers. Covers Latin tags, their Cyrillic look-alikes (ОВА/ОАД - real anime
            // releases write these in Cyrillic, e.g. "Сезон 1 + ОВА"), and the Ukrainian/transliterated specials
            // words in ALL their inflected forms: спецвипуск/спецвипуски/спецвипусків, спецепізод/спецепізоди,
            // спецсерії, спешл/спешли (the bare-plural-only forms used before silently dropped the common singulars).
            private static readonly Regex _specialsTagRegex = new Regex(@"\b(?:OVA|ONA|OAD|Specials|Special\s+Episodes?|ОВА|ОАД|спецвипуск\w*|спецепізод\w*|спецсері\w*|спешл\w*)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // Bracketed Cyrillic edition phrases mapped to the English edition word *arr custom formats key on. Only
            // matched INSIDE "[...]" (see ExtractExtras) so a Cyrillic title word with the same stem is never touched.
            private static readonly (Regex Stem, string Name)[] _cyrillicEditions =
            {
                (new Regex(@"режисерськ", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Director's Cut"),
                (new Regex(@"розширен|подовжен", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Extended"),
                (new Regex(@"театральн", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Theatrical"),
                (new Regex(@"коротш", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Shortened")
            };
            // A comma/semicolon separated list of episode ranges after an episode keyword, collapsed to a min-max envelope.
            private static readonly Regex _episodeListRegex = new Regex(@"((?:Серії|Серій|Серія|Епізоди?|Випуск\w*|Episodes?)\s*[:]*\s*)(\d{1,4}(?:-\d{1,4})?(?:(?:\s*[,;]\s*|\s+(?:and|та|і)\s+)\d{1,4}(?:-\d{1,4})?)+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // A "some episodes (33 of 84)" / "деякі/окремі/вибрані епізоди (33 з 84)" subset descriptor on a
            // multi-season pack - the count is across seasons, not season-relative, so it is dropped wholesale.
            private static readonly Regex _subsetDescriptorRegex = new Regex(@"(?:деякі|окремі|вибрані|some|selected)\s+(?:епізоди|серії|episodes?)(?:\s*\(?\s*\d+\s*(?:of|з|із)\s*\d+\s*\)?)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // A specials marker carrying its own episode numbers ("specials (episodes 9-10)", "+ спецвипуски 1-3"):
            // those numbers are SPECIALS, not main-season episodes, so the numbers are dropped (the "specials" word
            // survives as a marker that ExtractExtras turns into a "Specials" tag). Prevents the bonus episodes from
            // being mistaken for the main SxxExx.
            private static readonly Regex _specialsEpisodesRegex = new Regex(
                @"(special\w*|спец\w*)\s*\(?\s*(?:episodes?|епізоди?|серії|серій|випуск\w*)\s*[:]*\s*\d+(?:\s*[-,]\s*\d+)*\s*\)?",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // A comma/semicolon/plus separated list of seasons ("Сезон 1, 2" / "Сезони 1-6, 8-9" / "Сезон 1+2")
            // collapsed to a min-max range.
            private static readonly Regex _seasonListRegex = new Regex(@"((?:Сезон\w*|Seasons?)\s*[:]*\s*)(\d+(?:-\d+)?(?:\s*[,;+]\s*\d+(?:-\d+)?)+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // A NUMBER-first season list where the keyword follows ("1, 2 сезони" / "1, 2, 3 seasons"). Group 1 is the
            // number list, collapsed to S<min>-S<max> by CollapseSeasonListNumberFirst.
            private static readonly Regex _seasonListNumberFirstRegex = new Regex(@"\b(\d+(?:\s*,\s*\d+)+)\s*(?:сезон\w*|seasons?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // A COUNT before a specials marker ("+ 5 Спешлів", "+ 3 OVA") - the number is a count of specials, not a
            // season/episode. Drop the number; keep the marker (group 1) for ExtractExtras.
            private static readonly Regex _specialsCountRegex = new Regex(@"\+\s*\d+\s*(спешл\w*|спецсері\w*|спецепізод\w*|спецвипуск\w*|OVA|ONA|OAD|ОВА|ОАД|Specials?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // "N з M <keyword>" count with the keyword AFTER the of-total ("12 з 116 епізодів") -> E01-N.
            private static readonly Regex _episodeCountOfKeywordEndRegex = new Regex(@"\b(\d{1,3})\s*(?:з|із|of)\s*\d{1,4}\s*(?:сері[йіяї]+|епізод\w*|випуск\w*|episodes?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // Repeated season keyword ("Сезон 1; Сезон 2; Сезон 3, серії 1-10") - a multi-season pack listed with the
            // keyword before EACH number. Collapsed to a season range S<min>-S<max>; any trailing per-season episode
            // detail is consumed (it cannot be represented as a clean SxxExx across several seasons).
            // An optional per-season episode detail after the season number. Allows a count written BEFORE the
            // keyword ("Season 1, 7 episodes") as well as the keyword-first form ("Сезон 3, серії 1-10"); the
            // trailing number list is matched precisely so it never swallows the comma that separates the next season.
            private const string _epDetail = @"(?:\s*(?:\([^)]*\)|[,;]\s*\d*\s*(?:сері[йіяї]+|епізод\w*|випуск\w*|episode\w*|episodes?|series)(?:\s*[:]?\s*\d+(?:\s*[-,]\s*\d+)*)?(?:\s*(?:із|з|of)\s*\d+)?))*";
            private static readonly Regex _seasonRepeatRegex = new Regex(
                @"(?:Сезон\w*|Seasons?)\s*[:]*\s*\d+" + _epDetail + @"(?:\s*[;,+]\s*(?:Сезон\w*|Seasons?)\s*[:]*\s*\d+" + _epDetail + @")+",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // Combined "Season N, <episodes>" forms where the trailing episode count/range must NOT be read as a
            // season endpoint (the #1 source of bogus ranges like "S01-S57"). These run BEFORE the season-list
            // collapse. The episode keyword (серій/серії/серія/епізод/episodes/series) or an "of M / з M" total is
            // what marks the trailing numbers as episodes rather than seasons.
            // "сері[йіяї]+" tolerates the серій/серії/серія forms AND common typos (серіі, серіій).
            private const string _epKeyword = @"(?:сері[йіяї]+|епізод\w*|випуск\w*|episode\w*|episodes?|series)";
            // Season RANGE + trailing episode count -> keep only the season range ("Сезони 6-11, 124 серій" -> S6-11).
            private static readonly Regex _seasonRangeEpisodeCountRegex = new Regex(
                @"(?:Сезон\w*|Seasons?)\s*[:]*\s*(\d+)\s*-\s*(\d+)(?:\s*\+\s*\S+?)?\s*[,;]\s*\d+\s*(?:" + _epKeyword + @"\b(?:\s*(?:з|із|of)\s*\d+)?|(?:з|із|of)\s*\d+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // Single season + episode RANGE (keyword or "of M" AFTER the range) -> SxxEaa-Ebb.
            private static readonly Regex _seasonEpisodeRangeAfterRegex = new Regex(
                @"(?:Сезон\w*|Seasons?)\s*[:]*\s*(\d+)\s*[,;]\s*(\d+)\s*-\s*(\d+)\s*(?:" + _epKeyword + @"\b|(?:з|із|of)\s*\d+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // Single season + episode RANGE (keyword BEFORE the range, e.g. "Сезон 2, серії 1-10" or, with no
            // comma, "Сезон 1 серії 1-12" / "Сезон 1 Випуск 1-5") -> SxxEaa-Ebb. The comma is optional; the episode
            // keyword anchors the match.
            private static readonly Regex _seasonEpisodeRangeBeforeRegex = new Regex(
                @"(?:Сезон\w*|Seasons?)\s*[:]*\s*(\d+)\s*[,;]?\s*" + _epKeyword + @"[,:]?\s*(\d+)\s*-\s*(\d+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // Single season + a single episode COUNT ("Сезон 3, 44 серій з 52" = season 3, 44 episodes present) ->
            // expand to the released range "S03E01-E44" (consistent with the "Серій N з M" -> "E01-N" count form).
            private static readonly Regex _seasonEpisodeCountRegex = new Regex(
                @"(?:Сезон\w*|Seasons?)\s*[:]*\s*(\d+)\s*[,;]\s*(\d+)\s*" + _epKeyword + @"\b(?:\s*(?:з|із|of)\s*\d+)?",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // Single season + a single episode number with an "of M" total but NO episode keyword ("Сезон 1, 05 з 8"
            // = season 1, episode 5 of 8) -> SxxEyy.
            private static readonly Regex _seasonEpisodeSingleOfRegex = new Regex(
                @"(?:Сезон\w*|Seasons?)\s*[:]*\s*(\d+)\s*[,;]\s*(\d+)\s*(?:з|із|of)\s*\d+",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // Standalone episode COUNT with the keyword AFTER the number and no season prefix ("4 серії з 4",
            // "10 серій з 10") -> "E01-N".
            private static readonly Regex _episodeCountKeywordAfterRegex = new Regex(
                @"\b(\d+)\s*" + _epKeyword + @"\s*(?:з|із|of|/)\s*\d+",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // Standalone episode RANGE with the keyword AFTER it and no season prefix ("1-4 серії", "747-889 епізоди",
            // "Серія 11 - 22") -> Eaa-Ebb. Runs after the season+episode combos so it never steals their episodes.
            private static readonly Regex _episodeRangeKeywordAfterRegex = new Regex(
                @"\b(\d{1,4})\s*-\s*(\d{1,4})\s*" + _epKeyword + @"\b",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // Standalone "N-M з/of K" range with an "of-total" but NO keyword ("Air (1-11 з 13)") -> Eaa-Ebb. K is
            // capped at 3 digits so a year span ("1990-2011 of ...") can never be mistaken for episodes; a negative
            // lookbehind excludes disc/volume counts ("Discs 1-16 of 16", "Диски 1-16 з 16") which are not episodes.
            // The "of-total" after "з/of": a number, or an "XXX"/"???" unknown-total placeholder uploaders use when
            // the show is still airing ("5 з XXX серії").
            private const string _ofTotal = @"(?:\d{1,3}|[XxХх]{2,}|\?{2,})";
            private static readonly Regex _episodeRangeOfRegex = new Regex(
                @"(?<!(?:Disc|Диск|Vol|Том|Книг|DVD|CD)\w*\s*)\b(\d{1,3})\s*-\s*(\d{1,3})\s*(?:з|із|of)\s*" + _ofTotal + @"\b",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // Standalone single "N з/of K" with an of-total but NO keyword ("(12 з 12)", "(01 з 06)") -> E01-N. Same
            // disc/volume lookbehind as the range form; K capped at 3 digits so a "5 of 2020" year cannot match. A
            // 4-digit N is excluded too (a bare year). Runs after the singular-index/season/range forms.
            private static readonly Regex _episodeSingleOfRegex = new Regex(
                @"(?<!(?:Disc|Диск|Vol|Том|Книг|DVD|CD)\w*\s*)(?<!\d)\b(\d{1,3})\s*(?:з|із|of)\s*" + _ofTotal + @"\b(?!\s*-)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // Singular NOUN-first "серія N з M"/"серію N з M" = the single Nth episode (of M) -> EN (an index, not a
            // count). Only the singular я/ю noun forms; the genitive/plural "серій/серії" is a count handled below.
            private static readonly Regex _episodeSingleIndexOfRegex = new Regex(
                @"\bсері[яю]\b\s*[:]*\s*(\d{1,3})\s*(?:з|із|of)\s+\d{1,3}\b",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // Number-first episode COUNT with no "of" total ("13 серій", "26 епізодів", "10 випусків") -> E01-N. The
            // lookbehind avoids the tail of a range ("1-4 серії" -> the "4 серії" must NOT match), and the lookahead
            // defers the "N серій з M" of-total form to _episodeCountKeywordAfterRegex.
            private static readonly Regex _episodeCountNumberFirstRegex = new Regex(
                @"(?<!\d\s?-\s?)\b(\d{1,3})\s*-?\s*(?:сері[йї]|епізод(?:и|ів)?|випуск(?:и|ів)?|episodes)\b(?!\s*(?:з|із|of|/)\s*\d)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            // Count form: "Серій N з M" / "Episodes N of M" / "Серій: N/M" means N episodes (of M total) are
            // present, NOT "episode N". Single number only (a range like "1-24 з 27" is handled elsewhere). The
            // non-prefixed "E01-N" output is what the standalone-episode token recognizer expects.
            private static readonly Regex _tvTitleEpisodeCountRegex = new Regex(@"\b(?:сері[йіяї]+|епізод\w*|випуск\w*|episodes?)\s*[:]*\s*(\d+)\s*(?:з|із|of|/)\s*\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex _digitsRegex = new Regex(@"\d{1,4}", RegexOptions.Compiled);

            // Closed set of audio/subtitle language tokens used on Toloka (shared by the language detector and the
            // subtitle-separator below so both stay in sync). A closed set avoids matching 3-letter title fragments.
            private const string _languageAlternation = @"Ukrainian|Ukr|Eng|Jap|Jpn|Rus|Ger|Deu|Fre|Fra|Fr|Ita|Spa|Esp|Pol|Kor|Cze|Che|Chi|Chn|Dan|Swe|Tur|Turk|Hin|Por|Mul|Multi|Bel|Rum|Rom|Ice|Isl|Fin|Heb|Gre|Ell|Nor|Hun|Dut|Nld|Tha|Vie|Slo|Bul|Cro|Srp|Geo|Arm|Ara|Est|Lit|Lav";

            // Matches a language token with an optional "Nx" multi-dub prefix (e.g. "2xUkr" = two Ukrainian dubs).
            // Homoglyph normalization runs first, so "2хUkr" is already "2xUkr" here.
            private static readonly Regex _languageTokenRegex = new Regex(
                @"\b(?:(?:\d+\s*x|x\s*\d+)\s*)?(" + _languageAlternation + @")\b",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // Splits the audio portion (before) from the subtitle portion (after): the first pipe, or the first
            // "sub"/"subs"/"subtitle" keyword - INCLUDING a language token that immediately precedes it ("Ukr sub"),
            // so a subtitle language written before the keyword is not mis-read as audio (e.g. "Fre / Ukr sub").
            private static readonly Regex _subsSeparatorRegex = new Regex(
                @"\||(?<![A-Za-z])(?:(?:" + _languageAlternation + @")\s+)?sub(?:title)?s?(?![A-Za-z])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // Maps a Toloka language token to the canonical name Sonarr/Radarr parse into a language.
            private static readonly Dictionary<string, string> _languageNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ukr"] = "Ukrainian", ["Eng"] = "English", ["Jap"] = "Japanese", ["Jpn"] = "Japanese",
                ["Rus"] = "Russian", ["Ger"] = "German", ["Fre"] = "French", ["Fra"] = "French",
                ["Ita"] = "Italian", ["Spa"] = "Spanish", ["Pol"] = "Polish", ["Kor"] = "Korean",
                ["Cze"] = "Czech", ["Che"] = "Czech", ["Chi"] = "Chinese", ["Chn"] = "Chinese", ["Dan"] = "Danish",
                ["Swe"] = "Swedish", ["Tur"] = "Turkish", ["Turk"] = "Turkish", ["Hin"] = "Hindi",
                ["Por"] = "Portuguese", ["Mul"] = "MULTi", ["Multi"] = "MULTi", ["Fr"] = "French",
                ["Bel"] = "Belarusian", ["Rum"] = "Romanian", ["Rom"] = "Romanian", ["Ice"] = "Icelandic",
                ["Isl"] = "Icelandic", ["Fin"] = "Finnish", ["Heb"] = "Hebrew", ["Gre"] = "Greek", ["Ell"] = "Greek",
                ["Nor"] = "Norwegian", ["Hun"] = "Hungarian", ["Dut"] = "Dutch", ["Nld"] = "Dutch", ["Tha"] = "Thai",
                ["Vie"] = "Vietnamese", ["Slo"] = "Slovak", ["Bul"] = "Bulgarian", ["Cro"] = "Croatian",
                ["Srp"] = "Serbian", ["Geo"] = "Georgian", ["Arm"] = "Armenian", ["Ara"] = "Arabic",
                ["Est"] = "Estonian", ["Lit"] = "Lithuanian", ["Lav"] = "Latvian",
                ["Esp"] = "Spanish", ["Deu"] = "German", ["Ukrainian"] = "Ukrainian"
            };

            // Cyrillic glyphs that are visually identical to Latin letters and appear inside otherwise-Latin tech
            // tokens on Toloka (e.g. "1080р", "DVDRір-AVС", "2хUkr"). They must be normalised back to Latin before any
            // Latin-keyed matching or Cyrillic stripping, otherwise resolution/codec/source data is silently destroyed.
            private static readonly Dictionary<char, char> _homoglyphs = new Dictionary<char, char>
            {
                ['а'] = 'a', ['е'] = 'e', ['о'] = 'o', ['р'] = 'p', ['с'] = 'c', ['х'] = 'x', ['і'] = 'i', ['у'] = 'y',
                ['к'] = 'k', ['ѕ'] = 's', ['ј'] = 'j',
                ['А'] = 'A', ['В'] = 'B', ['Е'] = 'E', ['К'] = 'K', ['М'] = 'M', ['Н'] = 'H', ['О'] = 'O', ['Р'] = 'P',
                ['С'] = 'C', ['Т'] = 'T', ['У'] = 'Y', ['Х'] = 'X', ['І'] = 'I', ['Ј'] = 'J', ['Ѕ'] = 'S'
            };

            public string Parse(string title, ICollection<int> category, bool stripCyrillicLetters = true, string releaseGroup = null, bool? ukrainianAudioDefault = null, bool exactRanges = false)
            {
                // Drop invisible/format characters (zero-width, BOM, bidi marks, Unicode tag chars) that some
                // uploaders sneak into tokens - they split a source like "W{tag}EBDLRip" so it is no longer
                // recognised and leave a stray Latin fragment masquerading as the title. \p{Cf} = Unicode "Format".
                title = Regex.Replace(title, @"\p{Cf}", string.Empty);

                var isTv = IsAnyTvCategory(category);

                // Sport lives under the TV category tree but its pipe-delimited event titles (race stages, match
                // listings) are not Sonarr/Radarr-matchable and only get mangled by Cyrillic-strip + reconstruction.
                // Treat such categories as pass-through (keep the honest original) instead of reconstructing.
                var isReconstructable = (isTv || IsAnyMovieCategory(category)) && !IsPassthroughVideoCategory(category);

                // Homoglyph + dash normalization ONLY for reconstructable video. Non-video is passed through verbatim,
                // so converting an all-Cyrillic acronym there ("УР-1"->"YP-1") or an em-dash would corrupt the honest
                // original. REVERSE runs before FORWARD so a Cyrillic word with one Latin intrusion ("Усi") is repaired
                // to Cyrillic ("Усі") first and never grabbed by the forward pass ("Yci").
                if (isReconstructable)
                {
                    title = NormalizeReverseHomoglyphs(title);
                    title = NormalizeHomoglyphs(title);

                    // Fold superscript digits (²³¹…) so a stylized title like "[Rec]²" survives as "[Rec]2".
                    title = NormalizeSuperscripts(title);

                    // https://www.fileformat.info/info/unicode/category/Pd/list.htm
                    title = Regex.Replace(title, @"\p{Pd}", "-");
                }

                // Collapse a full release DATE in parentheses ("(03.10.2013)", "(2014.07.13)") to just its 4-digit
                // year so the year-anchored rebuild can use it (music/event releases label themselves by date).
                title = _dateToYearRegex.Replace(title, m => "(" + (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value) + ")");

                // Detect the primary AUDIO language from the still-intact title (the rebuild path drops language
                // clutter). Reading only the audio portion means a subtitle-only release is never mistagged.
                var audioLanguage = DetectAudioLanguage(title, ukrainianAudioDefault);

                // In "exact ranges" mode, capture the real (possibly gapped) episode/season lists BEFORE they are
                // collapsed to a single min-max envelope, so the collapsed token can be swapped back for the exact
                // ranges after the title is rebuilt. Only kept when the list actually has a gap (otherwise the
                // envelope already shows the truth).
                List<(int Start, int End)> exactEpisodes = null, exactSeasons = null;
                if (exactRanges && isTv)
                {
                    var epListMatch = _episodeListRegex.Match(title);
                    if (epListMatch.Success)
                    {
                        var ranges = ParseRanges(epListMatch.Groups[2].Value);
                        if (HasGap(ranges))
                        {
                            exactEpisodes = ranges;
                        }
                    }

                    var seasonListMatch = _seasonListRegex.Match(title);
                    if (seasonListMatch.Success)
                    {
                        var ranges = ParseRanges(seasonListMatch.Groups[2].Value);
                        if (HasGap(ranges))
                        {
                            exactSeasons = ranges;
                        }
                    }
                }

                // Preserve edition/part/specials markers that the rebuild path would otherwise discard.
                var extras = ExtractExtras(title);

                if (isTv && isReconstructable)
                {
                    // Drop a "some episodes (33 of 84)" / "деякі епізоди (33 з 84)" subset descriptor on a multi-season
                    // pack BEFORE any episode rule runs: its "33 of 84" is a cross-season count (not season-relative
                    // episodes), so it must not be mis-read as "E01-33" and must not leak into the series name.
                    title = _subsetDescriptorRegex.Replace(title, " ");

                    // Drop a DISC count/range ("Discs 1-16 of 16", "Диски 1-16 з 16", "DVDs 11 of 11") wholesale -
                    // disc numbers are not episodes, and a leftover "16 of 16" after the range would otherwise be
                    // re-read as an episode count.
                    title = _discCountRegex.Replace(title, " ");

                    // Drop an "except episodes N, M" clause so the excluded episode numbers don't become a range.
                    title = _exceptClauseRegex.Replace(title, " ");

                    // "(Mini Series)" / "(міні серіал)" -> the single season S01.
                    title = _miniSeriesRegex.Replace(title, " S01 ");

                    // Drop the episode numbers attached to a specials marker ("+ specials (episodes 9-10)") so the
                    // bonus episodes are not mistaken for the main season; "specials" itself becomes a Specials tag.
                    title = _specialsEpisodesRegex.Replace(title, " $1 ");

                    // Drop the COUNT before a specials marker ("Сезон 1 + 5 Спешлів" -> "Сезон 1 + Спешлів") so the
                    // "5" is not read as a season; the marker survives for ExtractExtras to tag as Specials.
                    title = _specialsCountRegex.Replace(title, " $1 ");

                    // Collapse disjoint episode lists ("серії 1-5,7,8" / "110-127, 138-167") to a min-max envelope
                    // FIRST, so the combos below see a single clean range instead of grabbing only the first one.
                    title = _episodeListRegex.Replace(title, CollapseEpisodeList);

                    // Collapse a NUMBER-first season list ("(1, 2 сезони)" / "1, 2, 3 seasons") to S<min>-S<max>
                    // before the count rules can misread the leading "1," and drop it.
                    title = _seasonListNumberFirstRegex.Replace(title, CollapseSeasonListNumberFirst);

                    // Collapse a repeated-keyword multi-season pack ("Сезон 1; Сезон 2; Сезон 3, ...") to S<min>-S<max>
                    // before the per-season episode detail can make the rebuild pick a single SxxExx for one season.
                    title = _seasonRepeatRegex.Replace(title, CollapseRepeatedSeasons);

                    // Disambiguate "Season N, <episodes>" combos so a trailing episode count/range is never mistaken
                    // for a season endpoint (the season-list collapse further below would otherwise do exactly that).
                    title = _seasonRangeEpisodeCountRegex.Replace(title, "S$1-$2");
                    title = _seasonEpisodeRangeAfterRegex.Replace(title, "S$1E$2-$3");
                    title = _seasonEpisodeRangeBeforeRegex.Replace(title, "S$1E$2-$3");
                    title = _seasonEpisodeCountRegex.Replace(title, "S$1E01-$2");
                    // "Сезон 1, 5 з 13" = season 1, 5 of 13 episodes available (Toloka counts cumulatively) -> the
                    // released range S01E01-E05. A genuine single episode ("1 з 8") collapses to S01E01 downstream.
                    title = _seasonEpisodeSingleOfRegex.Replace(title, "S$1E01-$2");

                    // Collapse a comma/semicolon list of seasons ("Сезон 1, 2") to a range so all seasons survive.
                    title = _seasonListRegex.Replace(title, CollapseSeasonList);

                    // Singular NOUN-first "серія N з M" / "серію N з M" = "episode N (of M total)" - a single episode
                    // index (the Nth), NOT a count. Emit EN. Runs before the count rule below so "серія 12 з 12"
                    // becomes E12, not the full E01-E12 pack. (The plural/genitive "серій/серії N з M" is a count and
                    // falls through to the rule below.)
                    title = _episodeSingleIndexOfRegex.Replace(title, "E$1");

                    // "N з M <keyword>" with the keyword AFTER the of-total ("12 з 116 епізодів") = N episodes of M
                    // total -> E01-N (using N, never M). Runs before the number-first count rule so "116 епізодів" is
                    // not grabbed as the count.
                    title = _episodeCountOfKeywordEndRegex.Replace(title, "E01-$1");

                    // "Серій N з M" / "Episodes N of M" is a count (N episodes of M total), not "episode N" - emit
                    // the released range E01-N (non-prefixed; downstream zero-pads and prefixes it to E01-EN).
                    title = _tvTitleEpisodeCountRegex.Replace(title, "E01-$1");

                    // Same count form but keyword AFTER the number ("4 серії з 4", "10 серій з 10") -> E01-N.
                    title = _episodeCountKeywordAfterRegex.Replace(title, "E01-$1");

                    // Number-first episode COUNT with no "of" total ("13 серій", "26 епізодів") -> E01-N. The
                    // lookbehind avoids grabbing the tail of a range ("1-4 серії") and the lookahead defers the
                    // "N серій з M" of-total form to the rule above.
                    title = _episodeCountNumberFirstRegex.Replace(title, "E01-$1");

                    title = _tvTitleCommaRegex.Replace(title, " $1-$2");
                    title = _tvTitleCyrillicXRegex.Replace(title, "$1XX$2");

                    // Season range: "Сезони 1-6" / "Seasons 1-6" -> "S1-6".
                    title = _tvTitleMultipleSeasonsRegex.Replace(title, "S$1");

                    // digit-before-keyword forms: "2 сезон" / "2 season" -> "S2"
                    title = _tvTitleSeasonNumberFirstRegex.Replace(title, "S$1");

                    title = _tvTitleUkrSeasonEpisodeOfRegex.Replace(title, "S$1E$2 of $3");
                    title = _tvTitleUkrSeasonEpisodeRegex.Replace(title, "S$1E$2");
                    title = _tvTitleUkrSeasonRegex.Replace(title, "S$1");
                    title = _tvTitleUkrEpisodeOfRegex.Replace(title, "E$1 of $2");
                    title = _tvTitleUkrEpisodeRegex.Replace(title, "E$1");

                    title = _tvTitleEngSeasonEpisodeOfRegex.Replace(title, "S$1E$2 of $3");
                    title = _tvTitleEngSeasonEpisodeRegex.Replace(title, "S$1E$2");
                    title = _tvTitleEngSeasonRegex.Replace(title, "S$1");
                    title = _tvTitleEngEpisodeOfRegex.Replace(title, "E$1 of $2");
                    title = _tvTitleEngEpisodeRegex.Replace(title, "E$1");

                    // Standalone "N-M <episode-keyword>" range with no season prefix ("1-4 серії", "747-889 епізоди").
                    title = _episodeRangeKeywordAfterRegex.Replace(title, "E$1-$2");

                    // Standalone "N-M з/of K" range with an of-total but no keyword ("Air (1-11 з 13)").
                    title = _episodeRangeOfRegex.Replace(title, "E$1-$2");

                    // Drop the "of M / з M" total that is attached to an episode token (e.g. "E01-24 of 27" -> "E01-24").
                    // Done here, while the total is anchored to its episode token, so CleanSeriesName never needs a
                    // blanket "of N" strip - which would wrongly eat "of 1" from a real title like "The Story of 1".
                    // MUST run before the bare-"N з M" rule below, so an already-built "E05 of 8" is reduced to "E05"
                    // and its digits are not re-read as a fresh "5 of 8" episode count.
                    title = Regex.Replace(title, @"((?:S\d{1,3})?E\d{1,3}(?:-\d{1,3})?)\s*[,;]?\s*(?:of|з|із)\s+[\dXxХх?]+", "$1", RegexOptions.IgnoreCase);

                    // Standalone single "N з/of K" with an of-total but no keyword ("(12 з 12)", "(01 з 06)") = N
                    // episodes available of K total -> E01-N (collapses to E01 when N==1). Runs LAST so the singular
                    // index, season, range and of-total forms above have already consumed their variants.
                    title = _episodeSingleOfRegex.Replace(title, "E01-$1");
                }

                // Source/codec normalization only for reconstructable video. Non-video (and sport) is passed through
                // verbatim, so normalizing there would just produce artefacts like a hyphenated "WEB-DL-x264".
                if (isReconstructable)
                {
                    title = NormalizeSourceCodec(title);
                }

                // Reconstruct into the canonical scene shape Sonarr/Radarr expect:
                //   Series Title SxxExx (Year) Source Resolution Codec   (group appended below)
                // Two candidates are considered, preferring the Cyrillic-stripped one (the Latin original title) but
                // FALLING BACK to the Cyrillic-kept one when stripping would destroy the name. This guarantees a
                // Cyrillic-only title is preserved whole rather than reduced to punctuation/quality "junk" (an
                // apostrophe from "Пам'ять", guillemets from a quoted concert name, a lone "2." from "ДНК 2.").
                // Only in strip mode (the default); strip=off keeps the original multi-language title.
                string rebuilt = null;
                if (isReconstructable && stripCyrillicLetters)
                {
                    var stripped = NormalizeSourceCodec(_stripCyrillicRegex.Replace(title, string.Empty).Trim(' ', '-'));

                    // Prefer the stripped form only when a genuine Latin title survives the strip; otherwise rebuild
                    // from the Cyrillic-kept title. TryBuildCleanReleaseTitle itself rejects a junk name (returns
                    // false), so even a stripped form that slips past HasLatinTitle falls through to the Cyrillic-kept
                    // rebuild rather than emitting junk.
                    if (HasLatinTitle(stripped) && TryBuildCleanReleaseTitle(stripped, isTv, out var fromStripped))
                    {
                        rebuilt = fromStripped;
                    }
                    else if (TryBuildCleanReleaseTitle(title, isTv, out var fromCyrillic))
                    {
                        rebuilt = fromCyrillic;
                    }
                }

                if (rebuilt != null)
                {
                    // Swap the collapsed min-max envelope for the exact (gapped) ranges when the user opted in.
                    rebuilt = ApplyExactRanges(rebuilt, exactEpisodes, exactSeasons);

                    title = AppendExtras(rebuilt, extras);

                    // Re-attach the detected audio language (dropped by the rebuild) so Sonarr/Radarr can score it.
                    title = AppendLanguage(title, audioLanguage);
                }
                else
                {
                    title = MoveFirstTagsToEndOfReleaseTitle(title);

                    // Moving a leading tag to the end can duplicate a tag that already appears in the title
                    // (e.g. a season/episode tag). Drop the redundant trailing copy.
                    title = RemoveDuplicateTrailingTag(title);

                    title = Regex.Replace(title, @"\(\s*\/\s*", "(");
                    title = Regex.Replace(title, @"\s*\/\s*\)", ")");

                    title = Regex.Replace(title, @"[\[\(]\s*[\)\]]", "");

                    title = title.Trim(_TitleTrimChars);

                    // Zero-pad season/episode numbers (S1->S01, E1-12->E01-12) for cleaner Sonarr parsing.
                    if (isTv)
                    {
                        title = ZeroPadSeasonEpisode(title);
                    }
                }

                // replace multiple spaces with a single space
                title = Regex.Replace(title, @"\s+", " ");

                title = title.Trim();

                // Append the release group so Sonarr/Radarr can detect it (e.g. "... WEB-DL-FanVoxUA").
                if (!string.IsNullOrEmpty(releaseGroup) && title.Length > 0)
                {
                    title = $"{title}-{releaseGroup}";
                }

                return title;
            }

            private static bool IsAnyTvCategory(ICollection<int> category) => category.Contains(TorznabCatType.TV.ID) || TorznabCatType.TV.SubCategories.Any(subCat => category.Contains(subCat.ID));

            private static bool IsAnyMovieCategory(ICollection<int> category) => category.Contains(TorznabCatType.Movies.ID) || TorznabCatType.Movies.SubCategories.Any(subCat => category.Contains(subCat.ID));

            public static bool IsAnyVideoCategory(ICollection<int> category) => IsAnyTvCategory(category) || IsAnyMovieCategory(category);

            // Video categories that should be passed through unchanged rather than reconstructed: sport events are
            // pipe-delimited listings with no clean series/movie name and are not Sonarr/Radarr-matchable.
            private static bool IsPassthroughVideoCategory(ICollection<int> category) => category.Contains(TorznabCatType.TVSport.ID);

            // Replaces Cyrillic homoglyphs with their Latin look-alikes, but only inside tokens that are predominantly
            // Latin/numeric (e.g. "1080р", "DVDRір-AVС") so genuine Cyrillic words are left untouched.
            private static string NormalizeHomoglyphs(string title)
            {
                if (string.IsNullOrEmpty(title) || !ContainsHomoglyph(title))
                {
                    return title;
                }

                var tokens = title.Split(' ');
                for (var i = 0; i < tokens.Length; i++)
                {
                    var token = tokens[i];
                    if (token.Length == 0)
                    {
                        continue;
                    }

                    int asciiLetter = 0, asciiDigit = 0, homoglyphCyr = 0, otherCyr = 0;
                    foreach (var ch in token)
                    {
                        if (ch < 128 && char.IsLetter(ch))
                        {
                            asciiLetter++;
                        }
                        else if (ch < 128 && char.IsDigit(ch))
                        {
                            asciiDigit++;
                        }
                        else if (_homoglyphs.ContainsKey(ch))
                        {
                            homoglyphCyr++;
                        }
                        else if (ch >= 0x0400 && ch <= 0x04FF)
                        {
                            otherCyr++;
                        }
                    }

                    // Convert when the token carries a homoglyph, has no non-homoglyph Cyrillic, AND is genuinely a
                    // Latin/numeric token rather than a Cyrillic word with a stray Latin glyph: either a real ASCII
                    // LETTER is present (a tech token like "WЕВDLRір-АVС") OR digits OUTNUMBER the homoglyphs
                    // ("1080р": 4 digits > 1). An all-Cyrillic acronym whose every letter is a homoglyph but that is
                    // mostly letters ("НТН", "УР-1", "Усi") fails this and is left for transliteration/reverse-fix.
                    if (homoglyphCyr == 0 || otherCyr > 0 || (asciiLetter == 0 && asciiDigit <= homoglyphCyr))
                    {
                        continue;
                    }

                    var sb = new System.Text.StringBuilder(token.Length);
                    foreach (var ch in token)
                    {
                        sb.Append(_homoglyphs.TryGetValue(ch, out var mapped) ? mapped : ch);
                    }

                    tokens[i] = sb.ToString();
                }

                return string.Join(" ", tokens);
            }

            private static bool ContainsHomoglyph(string value)
            {
                foreach (var ch in value)
                {
                    if (_homoglyphs.ContainsKey(ch))
                    {
                        return true;
                    }
                }

                return false;
            }

            // Superscript digits used in stylized sequel titles ("[Rec]²", "Alien³") -> ASCII so the number survives.
            private static readonly Dictionary<char, char> _superscripts = new Dictionary<char, char>
            {
                ['⁰'] = '0', ['¹'] = '1', ['²'] = '2', ['³'] = '3', ['⁴'] = '4',
                ['⁵'] = '5', ['⁶'] = '6', ['⁷'] = '7', ['⁸'] = '8', ['⁹'] = '9'
            };

            private static string NormalizeSuperscripts(string title)
            {
                if (string.IsNullOrEmpty(title))
                {
                    return title;
                }

                System.Text.StringBuilder sb = null;
                for (var i = 0; i < title.Length; i++)
                {
                    if (_superscripts.TryGetValue(title[i], out var digit))
                    {
                        sb ??= new System.Text.StringBuilder(title);
                        sb[i] = digit;
                    }
                }

                return sb?.ToString() ?? title;
            }

            // Normalizes a person/group NAME (uploader / release group) by swapping Cyrillic homoglyphs that
            // masquerade as Latin letters for their Latin look-alikes — e.g. "wаrden" -> "warden", "Аlех" -> "Alex".
            // Unlike the title-oriented NormalizeHomoglyphs it does NOT require the homoglyphs to be a minority of the
            // token (a short handle can be mostly look-alikes), but a token carrying any NON-homoglyph Cyrillic letter
            // is treated as a genuine Cyrillic word and left untouched, so real Cyrillic handles survive (search) and
            // are phonetically transliterated downstream (release group) instead of being garbled into look-alikes.
            public static string NormalizeNameHomoglyphs(string name)
            {
                if (string.IsNullOrEmpty(name) || !ContainsHomoglyph(name))
                {
                    return name;
                }

                var tokens = name.Split(' ');
                for (var i = 0; i < tokens.Length; i++)
                {
                    var token = tokens[i];
                    if (token.Length == 0)
                    {
                        continue;
                    }

                    // Convert only a token that is a genuine Latin-script word with Cyrillic look-alikes sneaked in
                    // (has at least one real ASCII letter) and carries no NON-homoglyph Cyrillic. An all-Cyrillic
                    // token — even if every letter has a Latin look-alike (e.g. "НТН", "Ріа") — is a real Cyrillic
                    // word/acronym and is left for transliteration, never garbled into look-alikes ("НТН" -> "HTH").
                    var hasAsciiLetter = false;
                    var hasOtherCyrillic = false;
                    foreach (var ch in token)
                    {
                        if (ch < 128 && char.IsLetter(ch))
                        {
                            hasAsciiLetter = true;
                        }
                        else if (ch >= 0x0400 && ch <= 0x04FF && !_homoglyphs.ContainsKey(ch))
                        {
                            hasOtherCyrillic = true;
                            break;
                        }
                    }

                    if (!hasAsciiLetter || hasOtherCyrillic)
                    {
                        continue;
                    }

                    var sb = new System.Text.StringBuilder(token.Length);
                    foreach (var ch in token)
                    {
                        sb.Append(_homoglyphs.TryGetValue(ch, out var mapped) ? mapped : ch);
                    }

                    tokens[i] = sb.ToString();
                }

                return string.Join(" ", tokens);
            }

            // Latin look-alikes -> their Cyrillic counterparts, for restoring a Cyrillic word that had Latin glyphs
            // sneaked in (the inverse of _homoglyphs). Only the unambiguous common pairs are mapped (s/j are omitted
            // - their Cyrillic look-alikes are rare and risky).
            private static readonly Dictionary<char, char> _reverseHomoglyphs = new Dictionary<char, char>
            {
                ['a'] = 'а', ['e'] = 'е', ['o'] = 'о', ['p'] = 'р', ['c'] = 'с', ['x'] = 'х', ['i'] = 'і', ['y'] = 'у', ['k'] = 'к',
                ['A'] = 'А', ['B'] = 'В', ['E'] = 'Е', ['K'] = 'К', ['M'] = 'М', ['H'] = 'Н', ['O'] = 'О', ['P'] = 'Р',
                ['C'] = 'С', ['T'] = 'Т', ['Y'] = 'У', ['X'] = 'Х', ['I'] = 'І'
            };

            private static bool ContainsReverseHomoglyph(string value)
            {
                foreach (var ch in value)
                {
                    if (_reverseHomoglyphs.ContainsKey(ch))
                    {
                        return true;
                    }
                }

                return false;
            }

            // Restores Latin look-alikes to Cyrillic, but ONLY inside a token that is a genuine Cyrillic word with a
            // few Latin glyphs sneaked in: it must have at least one real Cyrillic letter and at least one Latin
            // look-alike, and NO non-look-alike Latin letter (which would prove it is really a Latin word, e.g.
            // "iPhone"). This is the inverse of NormalizeHomoglyphs and never touches an ASCII tech token.
            private static string NormalizeReverseHomoglyphs(string title)
            {
                if (string.IsNullOrEmpty(title) || !ContainsReverseHomoglyph(title))
                {
                    return title;
                }

                var tokens = title.Split(' ');
                for (var i = 0; i < tokens.Length; i++)
                {
                    var token = tokens[i];
                    if (token.Length == 0)
                    {
                        continue;
                    }

                    int cyrillic = 0, latinLookalike = 0, latinOther = 0;
                    foreach (var ch in token)
                    {
                        if (ch >= 0x0400 && ch <= 0x04FF)
                        {
                            cyrillic++;
                        }
                        else if (ch < 128 && char.IsLetter(ch))
                        {
                            if (_reverseHomoglyphs.ContainsKey(ch))
                            {
                                latinLookalike++;
                            }
                            else
                            {
                                latinOther++;
                            }
                        }
                    }

                    if (cyrillic == 0 || latinLookalike == 0 || latinOther > 0)
                    {
                        continue;
                    }

                    var sb = new System.Text.StringBuilder(token.Length);
                    foreach (var ch in token)
                    {
                        sb.Append(_reverseHomoglyphs.TryGetValue(ch, out var mapped) ? mapped : ch);
                    }

                    tokens[i] = sb.ToString();
                }

                return string.Join(" ", tokens);
            }

            // Collects edition/part/specials markers (PROPER, Director's Cut, Part 2, OVA, ...) as an ordered,
            // de-duplicated list so the rebuild path can re-append the ones it dropped without data loss.
            private static List<string> ExtractExtras(string title)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var parts = new List<string>();

                foreach (Match m in _editionTagRegex.Matches(title))
                {
                    var token = NormalizeEditionToken(m.Value);
                    if (seen.Add(token))
                    {
                        parts.Add(token);
                    }
                }

                // Cyrillic edition phrases written inside brackets ("[Коротша версія]" -> "Shortened"). Restricted to
                // bracketed content so a Cyrillic title word sharing a stem is never mistaken for an edition marker.
                foreach (Match m in Regex.Matches(title, @"\[([^\[\]]+)\]"))
                {
                    var content = m.Groups[1].Value;
                    foreach (var edition in _cyrillicEditions)
                    {
                        if (edition.Stem.IsMatch(content) && seen.Add(edition.Name))
                        {
                            parts.Add(edition.Name);
                            break;
                        }
                    }
                }

                foreach (Match m in _partTagRegex.Matches(title))
                {
                    var number = Regex.Replace(m.Groups[1].Value, @"\s*-\s*", "-");
                    number = Regex.Replace(number, @"\s*,\s*", ", ");
                    var token = (number.Contains('-') || number.Contains(',') ? "Parts " : "Part ") + number;
                    if (seen.Add(token))
                    {
                        parts.Add(token);
                    }
                }

                foreach (Match m in _specialsTagRegex.Matches(title))
                {
                    var token = NormalizeSpecialsToken(m.Value);
                    if (seen.Add(token))
                    {
                        parts.Add(token);
                    }
                }

                return parts;
            }

            // Maps any specials marker - Latin (OVA/ONA/OAD/Special), the Cyrillic look-alikes (ОВА/ОАД) and the
            // Ukrainian/transliterated words (спецвипуск.../спецепізод.../спецсерії/спешл...) - to a single canonical
            // token, so distinct specials releases don't collide with the main season and *arr edition matching is
            // consistent regardless of how the uploader spelled the marker.
            private static string NormalizeSpecialsToken(string raw)
            {
                var v = raw.ToLowerInvariant();
                if (v.StartsWith("спец", StringComparison.Ordinal) ||
                    v.StartsWith("спешл", StringComparison.Ordinal) ||
                    v.StartsWith("special", StringComparison.Ordinal))
                {
                    return "Specials";
                }

                // Latin and Cyrillic look-alike anime tags.
                if (v == "ova" || v == "ова")
                {
                    return "OVA";
                }

                if (v == "ona" || v == "она")
                {
                    return "ONA";
                }

                if (v == "oad" || v == "оад")
                {
                    return "OAD";
                }

                return char.ToUpperInvariant(raw[0]) + raw.Substring(1).ToLowerInvariant();
            }

            // Appends each extra only if it is not already present in the rebuilt title (a Part/edition marker can
            // survive in the series name when it sits before the year), avoiding duplicate tokens.
            private static string AppendExtras(string rebuilt, List<string> extras)
            {
                if (extras == null || extras.Count == 0)
                {
                    return rebuilt;
                }

                var sb = new System.Text.StringBuilder(rebuilt);
                foreach (var extra in extras)
                {
                    if (rebuilt.IndexOf(extra, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    // A Part/Parts extra is redundant when the name already carries part info under a synonym the
                    // rebuilt title kept verbatim - Cyrillic "Частина 1-2", a Latin "Vol. 1"/"Том 1", an English
                    // "Part One", or a roman "Vol. I-II". Appending "Part 1" then would duplicate it. Skip when the
                    // name has the same number OR already carries any Part/Vol/Частина word (covers roman/spelled-out
                    // numbers the digit check would miss).
                    if (extra.StartsWith("Part", StringComparison.OrdinalIgnoreCase))
                    {
                        var number = extra.Substring(extra.IndexOf(' ') + 1).Trim();
                        if (Regex.IsMatch(rebuilt, @"(?:Частин|Части|Vol|Том|Part|Cour)\w*\.?\s*[:]*\s*" + Regex.Escape(number), RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(rebuilt, @"\b(?:Vol|Том)\b|\bPart\s+(?:One|Two|Three|Four|[IVX]+)\b|\bЧастин[аиі]\b", RegexOptions.IgnoreCase))
                        {
                            continue;
                        }
                    }

                    // A "Specials" extra is redundant when the series name itself already contains "Special" (e.g.
                    // "Top Gear. India Special"); appending it would read "India Special ... Specials".
                    if (extra.Equals("Specials", StringComparison.OrdinalIgnoreCase) &&
                        Regex.IsMatch(rebuilt, @"\bSpecials?\b", RegexOptions.IgnoreCase))
                    {
                        continue;
                    }

                    sb.Append(' ').Append(extra);
                }

                return sb.ToString();
            }

            // Determines the primary AUDIO language for Sonarr/Radarr scoring without misreading subtitle-only
            // releases. The audio portion is everything before the first subtitle separator (pipe or "sub"
            // keyword); a Japanese film with Ukrainian subs ("Jap | Sub Ukr") therefore reports Japanese, never
            // Ukrainian. Ukrainian wins when present (the value-add on this tracker). When the title carries no
            // audio token at all, the forum convention decides (dub trees default to Ukrainian; the subtitle tree
            // defaults to original audio, i.e. no Ukrainian tag).
            private static string DetectAudioLanguage(string title, bool? ukrainianAudioDefault)
            {
                if (string.IsNullOrWhiteSpace(title))
                {
                    return null;
                }

                // Find the year first, then split audio|subs only WITHIN the post-year metadata block. This keeps a
                // "|" used as a TITLE separator (anime "Localized | Romaji") from being mistaken for the audio|sub
                // divider, and means a title word that coincides with a 3-letter code ("Dan" in "Dance with Dan",
                // before the year) can never win.
                var year = _yearAnchorRegex.Match(title);
                var afterYear = year.Success ? title.Substring(year.Index + year.Length) : title;
                var sep = _subsSeparatorRegex.Match(afterYear);
                var scanRegion = sep.Success ? afterYear.Substring(0, sep.Index) : afterYear;

                string firstForeign = null;
                var hasToken = false;
                foreach (Match m in _languageTokenRegex.Matches(scanRegion))
                {
                    hasToken = true;
                    var code = m.Groups[1].Value;
                    // StartsWith so the full word "Ukrainian" also resolves (not only the "Ukr" abbreviation).
                    if (code.StartsWith("Ukr", StringComparison.OrdinalIgnoreCase))
                    {
                        return "Ukrainian";
                    }

                    if (firstForeign == null && _languageNames.TryGetValue(code, out var name))
                    {
                        firstForeign = name;
                    }
                }

                if (hasToken)
                {
                    return firstForeign;
                }

                // No explicit audio tokens: defer to the forum's language convention.
                return ukrainianAudioDefault == true ? "Ukrainian" : null;
            }

            // Appends the audio-language token unless the title already carries it.
            private static string AppendLanguage(string title, string language)
            {
                if (string.IsNullOrEmpty(language) || string.IsNullOrEmpty(title))
                {
                    return title;
                }

                // Only skip when the language is already the trailing tag - checking anywhere would wrongly suppress
                // the tag for a title whose NAME contains the language word (e.g. "Yamishibai: Japanese Ghost Stories").
                return title.TrimEnd().EndsWith(language, StringComparison.OrdinalIgnoreCase)
                    ? title
                    : title + " " + language;
            }

            private static string NormalizeEditionToken(string value)
            {
                var collapsed = Regex.Replace(value, @"\s+", " ").Trim();
                switch (collapsed.ToUpperInvariant())
                {
                    case "PROPER":
                    case "REPACK":
                    case "RERIP":
                    case "IMAX":
                        return collapsed.ToUpperInvariant();
                    case "REMASTER":
                    case "REMASTERED":
                        return "REMASTERED";
                    case "UNCUT":
                        return "Uncut";
                    case "UNRATED":
                        return "Unrated";
                    default:
                        if (collapsed.StartsWith("Director", StringComparison.OrdinalIgnoreCase))
                        {
                            return "Director's Cut";
                        }

                        if (collapsed.StartsWith("AI", StringComparison.OrdinalIgnoreCase))
                        {
                            return "AI Remastered";
                        }

                        if (collapsed.StartsWith("Special", StringComparison.OrdinalIgnoreCase))
                        {
                            return "Special Edition";
                        }

                        if (collapsed.StartsWith("Collector", StringComparison.OrdinalIgnoreCase))
                        {
                            return "Collector's Edition";
                        }

                        if (collapsed.StartsWith("Limited", StringComparison.OrdinalIgnoreCase))
                        {
                            return "Limited Edition";
                        }

                        if (collapsed.StartsWith("Silent", StringComparison.OrdinalIgnoreCase))
                        {
                            return "Silent Version";
                        }

                        if (collapsed.StartsWith("Open", StringComparison.OrdinalIgnoreCase))
                        {
                            return "Open Matte";
                        }

                        if (collapsed.StartsWith("Theatrical", StringComparison.OrdinalIgnoreCase))
                        {
                            return "Theatrical";
                        }

                        if (collapsed.StartsWith("Extended", StringComparison.OrdinalIgnoreCase))
                        {
                            return "Extended";
                        }

                        if (collapsed.IndexOf("Complete Restored", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return "Complete Restored Edition";
                        }

                        if (collapsed.StartsWith("Recut", StringComparison.OrdinalIgnoreCase))
                        {
                            return "Recut";
                        }

                        if (collapsed.IndexOf("Cut", StringComparison.OrdinalIgnoreCase) >= 0 && collapsed.StartsWith("Euro", StringComparison.OrdinalIgnoreCase))
                        {
                            return "European Cut";
                        }

                        if (collapsed.StartsWith("AI", StringComparison.OrdinalIgnoreCase) && collapsed.IndexOf("Upscale", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return "AI Upscaled";
                        }

                        return collapsed;
                }
            }

            // Collapses a year value ("(2019)", "(2001/2010)", "(1997,2000,02,06)") to a single year or a
            // "first-last" span. A 2-digit year in a collection list is expanded against the century (02 -> 2002).
            private static string CollapseYears(string yearMatchValue)
            {
                var years = Regex.Matches(yearMatchValue, @"\d{2,4}")
                    .Select(m => ExpandYear(m.Value))
                    .Where(y => y > 0)
                    .ToList();

                if (years.Count == 0)
                {
                    return yearMatchValue.Trim('(', '[', ')', ']').Trim();
                }

                return years.Min() == years.Max()
                    ? years.Min().ToString(CultureInfo.InvariantCulture)
                    : $"{years.Min()}-{years.Max()}";
            }

            private static int ExpandYear(string token)
            {
                if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                {
                    return 0;
                }

                if (token.Length == 4)
                {
                    return n >= 1900 && n <= 2099 ? n : 0;
                }

                if (token.Length == 2)
                {
                    return n <= 30 ? 2000 + n : 1900 + n;
                }

                return 0;
            }

            // Replaces a comma/semicolon separated episode list with a single "first-last" envelope.
            private static string CollapseEpisodeList(Match match)
            {
                var keyword = match.Groups[1].Value;
                var numbers = _digitsRegex.Matches(match.Groups[2].Value)
                    .Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture))
                    .ToList();

                if (numbers.Count == 0)
                {
                    return match.Value;
                }

                var min = numbers.Min();
                var max = numbers.Max();
                return keyword + (min == max ? min.ToString(CultureInfo.InvariantCulture) : $"{min}-{max}");
            }

            // Parses "1-8, 11-26, 30" into an ordered list of (start, end) ranges (a single number => start==end).
            private static List<(int Start, int End)> ParseRanges(string value)
            {
                var ranges = new List<(int, int)>();
                foreach (Match m in Regex.Matches(value, @"(\d+)(?:\s*-\s*(\d+))?"))
                {
                    var start = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    var end = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : start;
                    ranges.Add((Math.Min(start, end), Math.Max(start, end)));
                }

                return ranges;
            }

            // True when the ranges do not cover every number between the overall min and max (i.e. there is a gap),
            // which is the only case where the exact list differs from the min-max envelope.
            private static bool HasGap(List<(int Start, int End)> ranges)
            {
                if (ranges == null || ranges.Count < 2)
                {
                    return false;
                }

                var covered = new HashSet<int>();
                foreach (var (start, end) in ranges)
                {
                    for (var i = start; i <= end; i++)
                    {
                        covered.Add(i);
                    }
                }

                var min = ranges.Min(r => r.Start);
                var max = ranges.Max(r => r.End);
                return covered.Count < (max - min + 1);
            }

            // Replaces the rebuilt min-max envelope token with the exact (gapped) ranges, preserving any season
            // prefix on an episode token (e.g. "S01E01-E26" -> "S01E01-E08, S01E11-E26"; "S01-S09" -> "S01-S06, S08-S09").
            private static string ApplyExactRanges(string rebuilt, List<(int Start, int End)> exactEpisodes, List<(int Start, int End)> exactSeasons)
            {
                if (exactEpisodes != null)
                {
                    rebuilt = Regex.Replace(rebuilt, @"(S\d{2,3})?E\d{2,3}-E\d{2,3}", m =>
                    {
                        var seasonPrefix = m.Groups[1].Success ? m.Groups[1].Value : string.Empty;
                        return string.Join(", ", exactEpisodes.Select(r =>
                            r.Start == r.End
                                ? $"{seasonPrefix}E{r.Start:D2}"
                                : $"{seasonPrefix}E{r.Start:D2}-E{r.End:D2}"));
                    }, RegexOptions.None);
                }

                if (exactSeasons != null)
                {
                    rebuilt = Regex.Replace(rebuilt, @"S\d{2,3}-S\d{2,3}", m =>
                        string.Join(", ", exactSeasons.Select(r =>
                            r.Start == r.End ? $"S{r.Start:D2}" : $"S{r.Start:D2}-S{r.End:D2}")), RegexOptions.None);
                }

                return rebuilt;
            }

            // Replaces a comma/semicolon separated season list with a single "first-last" range.
            private static string CollapseSeasonList(Match match)
            {
                var keyword = match.Groups[1].Value;
                var numbers = _digitsRegex.Matches(match.Groups[2].Value)
                    .Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture))
                    .ToList();

                if (numbers.Count == 0)
                {
                    return match.Value;
                }

                var min = numbers.Min();
                var max = numbers.Max();
                return keyword + (min == max ? min.ToString(CultureInfo.InvariantCulture) : $"{min}-{max}");
            }

            // Collapses a NUMBER-first season list ("1, 2 сезони") to the Sonarr "S<min>-S<max>" form.
            private static string CollapseSeasonListNumberFirst(Match match)
            {
                var numbers = _digitsRegex.Matches(match.Groups[1].Value)
                    .Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture))
                    .ToList();

                if (numbers.Count == 0)
                {
                    return match.Value;
                }

                var min = numbers.Min();
                var max = numbers.Max();
                // Emit the single-S range form "S1-2" (NOT "S1-S2") so the SxxExx tokenizer reads it as one token;
                // ZeroPadSeasonEpisode then renders it as the Sonarr-preferred "S01-S02".
                return min == max ? $"S{min}" : $"S{min}-{max}";
            }

            // Collapses a repeated-keyword season list ("Сезон 1; Сезон 2; Сезон 3, серії 1-10") to "S<min>-<max>",
            // reading only the numbers that directly follow a season keyword (never the trailing episode numbers).
            private static readonly Regex _seasonKeywordNumberRegex = new Regex(@"(?:Сезон\w*|Seasons?)\s*[:]*\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static string CollapseRepeatedSeasons(Match match)
            {
                var numbers = _seasonKeywordNumberRegex.Matches(match.Value)
                    .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                    .ToList();

                if (numbers.Count == 0)
                {
                    return match.Value;
                }

                var min = numbers.Min();
                var max = numbers.Max();
                return min == max ? $"S{min}" : $"S{min}-{max}";
            }

            // Normalizes source/codec spellings to the tokens Sonarr/Radarr parse, before the rebuild reads them.
            private static string NormalizeSourceCodec(string title)
            {
                title = Regex.Replace(title, @"\b-Rip\b", "Rip", RegexOptions.IgnoreCase);
                title = Regex.Replace(title, @"\bHDTVRip\b", "HDTV", RegexOptions.IgnoreCase);
                // "WEB-DLRip"/"WEBDLRip" is a re-encode of a WEB-DL (MediaInfo-confirmed) -> WEBRip, the bucket
                // Sonarr/Radarr actually parse it into (the raw "WEB-DLRip" token parses as nothing).
                title = Regex.Replace(title, @"\bWEB-?DLRip\b", "WEBRip", RegexOptions.IgnoreCase);
                title = Regex.Replace(title, @"\bWEBDL\b", "WEB-DL", RegexOptions.IgnoreCase);

                // Normalize codecs so Sonarr/Radarr recognise them (and don't mistake "AVC" for the release group).
                title = _codecAvcRegex.Replace(title, "x264");
                title = _codecHevcRegex.Replace(title, "x265");

                // A bare resolution number written without the "p"/"i" suffix right after a source ("BDRip 1080",
                // "WEB-DL 1080 Eng") -> add the "p" so the rebuild's resolution detector picks it up (else *arr
                // defaults to 480p). The source lookbehind keeps a random number in a title from being touched.
                title = Regex.Replace(title, @"(?<=(?:Rip|DL|HDTV|HDV|DVD|Ray|Remux|WEB|BD|HD|UHD|CAM|SDTV|PDTV)[^\d\n]{1,4})(480|576|720|1080|1440|2160)\b(?![pi\d])", "$1p", RegexOptions.IgnoreCase);
                return title;
            }

            // Editorial/transfer notes and bare source words that look like Latin "title" words but are not, so they
            // must not make a Cyrillic-only release pass the HasLatinTitle gate (e.g. "[ENG Transfer]", "Sub Eng",
            // a bare "DVB"/"TS"/"CAM"). The title-real check below removes these before counting Latin letters.
            private static readonly Regex _nonTitleLatinNoiseRegex = new Regex(
                @"\b(?:AVC|HEVC|H\.?\s?26[45]|x26[45]|Rip|WEB-?DLRip|HDTVRip|HDRip|DVDRip|DVD|DVB|TS|CAM|TV|HD|SD|FHD|UHD|VCD|HDV|Sub|Subs|Subtitle|Subtitles|Dub|Dubbed|MVO|DVO|AVO|VO|Transfer|Voice|Audio|Remux|Upscale|Open\s*Matte|\d+\s*x|x\s*\d+|\d+fps|fps|kbps|bit|" +
                @"Classic|Classical|Pop|Rock|Jazz|Blues|Folk|Metal|Punk|Disco|Techno|Trance|Rap|Funk|Instrumental|Acoustic|OST|Soundtrack|Reggae|Ambient|Indie|Hip-?Hop|Ska|Bard|Karaoke|Ethno|Electro|Electronic|House|Chanson|World|Romance|Bonus|Live|Concert|Unplugged|Mix|Remix|VA)\b",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // True when, after removing language/source/quality/edition clutter, a real Latin title word remains.
            private static bool HasLatinTitle(string stripped)
            {
                if (string.IsNullOrWhiteSpace(stripped))
                {
                    return false;
                }

                // Remove only the bracket CHARACTERS (keep their contents), so the editorial-tag words inside survive
                // to be filtered by the quality/edition/language/noise passes below ("[ENG Transfer]" -> "ENG
                // Transfer" -> both removed as language+noise -> not a title), while a stylized title that lives
                // inside brackets ("[Rec]2" -> "Rec 2") keeps its real word and correctly counts as a Latin title.
                var residue = Regex.Replace(stripped, @"[\[\]]", " ");
                residue = _qualityTokenRegex.Replace(residue, " ");
                residue = _editionTagRegex.Replace(residue, " ");
                // Remove ALL language tokens via the shared detector, which also strips the "Nx" multi-dub prefix, so
                // "2xUkr" cannot leave a stray "xUkr" that masquerades as the title for a Cyrillic-only name.
                residue = _languageTokenRegex.Replace(residue, " ");
                // Remove codecs, bare source words and the residual "Rip"/"Sub"/"Dub"/"Transfer" clutter.
                residue = _nonTitleLatinNoiseRegex.Replace(residue, " ");

                // A surviving run of >=2 Latin letters means a genuine title remains.
                return Regex.IsMatch(residue, "[A-Za-z]{2,}");
            }

            private static string ZeroPadSeasonEpisode(string input)
            {
                // Combined SxxExx (episode E is preceded by a digit, so it has no word boundary - handle it together).
                input = Regex.Replace(input, @"\bS(\d{1,3})(?:-(\d{1,3}))?E(\d{1,3})(?:-(\d{1,3}))?", m =>
                    "S" + PadNumber(m.Groups[1].Value) + (m.Groups[2].Success ? "-" + PadNumber(m.Groups[2].Value) : "") +
                    "E" + PadNumber(m.Groups[3].Value) + (m.Groups[4].Success ? "-" + PadNumber(m.Groups[4].Value) : ""));

                // Standalone season (no episode part).
                input = Regex.Replace(input, @"\bS(\d{1,3})(?:-(\d{1,3}))?", m =>
                    "S" + PadNumber(m.Groups[1].Value) + (m.Groups[2].Success ? "-" + PadNumber(m.Groups[2].Value) : ""));

                // Standalone episode (preceded by a boundary, e.g. "E14 of 20").
                input = Regex.Replace(input, @"\bE(\d{1,3})(?:-(\d{1,3}))?", m =>
                    "E" + PadNumber(m.Groups[1].Value) + (m.Groups[2].Success ? "-" + PadNumber(m.Groups[2].Value) : ""));

                // Multi-season packs: emit Sonarr's preferred "S01-S02" form (only for a standalone season range,
                // never the combined "S01-02E.." which Sonarr reads differently).
                input = Regex.Replace(input, @"\bS(\d{2,3})-(\d{2,3})(?![\dEe])", "S$1-S$2");

                return input;
            }

            private static string PadNumber(string number) => int.TryParse(number, out var n) ? n.ToString("D2") : number;

            // Rebuilds a release title into the canonical order Sonarr/Radarr parse best:
            //   "Series Title SxxExx (Year) Source Resolution Codec".
            // The season/episode token is placed right after the series name (Sonarr treats everything
            // before it as the series title), language/subtitle clutter is dropped, and only a single
            // Latin title is kept. A yearless release anchors on the first source/resolution token instead.
            // Returns false when there is neither a year nor a source token to anchor on (caller falls back).
            private static bool TryBuildCleanReleaseTitle(string title, bool isTv, out string result)
            {
                result = null;

                string head, tail, year;
                // Anchor on the LAST year: Toloka lists "Localized (Year) / Original (Year)" with the year repeated
                // per segment, so anchoring on the first would strip the head down to the Cyrillic localized segment
                // and leave the Latin original title in the tail. The last year keeps both segments in the head so
                // CleanSeriesName can pick the Latin one.
                var yearMatches = _yearAnchorRegex.Matches(title);
                if (yearMatches.Count > 0)
                {
                    var yearMatch = yearMatches[yearMatches.Count - 1];
                    head = title.Substring(0, yearMatch.Index);
                    tail = title.Substring(yearMatch.Index + yearMatch.Length);
                    year = CollapseYears(yearMatch.Value);
                }
                else if (_bareYearAnchorRegex.Match(title) is { Success: true } bareYear)
                {
                    // A bare year right before the source/resolution block is a real year ("...2019 480p"); anchor on
                    // it so the year survives and a numeric segment ("2019") cannot take the series name's place.
                    head = title.Substring(0, bareYear.Index);
                    tail = title.Substring(bareYear.Index + bareYear.Length);
                    year = bareYear.Value.Trim();
                }
                else
                {
                    // No year to anchor on (common on Toloka): split on the first source/resolution token so the
                    // release still becomes "Series [SxxExx] Source Resolution Codec" with language/subtitle clutter
                    // dropped, instead of falling back to the raw multi-language title.
                    var sourceMatch = _sourceAnchorRegex.Match(title);
                    if (!sourceMatch.Success)
                    {
                        return false;
                    }

                    head = title.Substring(0, sourceMatch.Index);
                    tail = title.Substring(sourceMatch.Index);
                    year = null;
                }

                // Pick the most specific season/episode token, searching head then tail. Prefer a combined
                // SxxExx; otherwise merge a standalone season with a standalone episode token.
                string seToken = null;
                if (isTv)
                {
                    string combined = null, seasonOnly = null, episodeOnly = null;
                    foreach (var source in new[] { head, tail })
                    {
                        foreach (Match m in _seasonEpisodeTokenRegex.Matches(source))
                        {
                            var v = m.Value;
                            var hasS = v.StartsWith("S", StringComparison.OrdinalIgnoreCase);
                            var hasE = v.IndexOf('E') >= 0 || v.IndexOf('e') >= 0;
                            if (hasS && hasE)
                            {
                                if (combined == null || v.Length > combined.Length)
                                {
                                    combined = v;
                                }
                            }
                            else if (hasS)
                            {
                                if (seasonOnly == null || v.Length > seasonOnly.Length)
                                {
                                    seasonOnly = v;
                                }
                            }
                            else if (hasE && (episodeOnly == null || v.Length > episodeOnly.Length))
                            {
                                episodeOnly = v;
                            }
                        }
                    }

                    seToken = combined
                              ?? (seasonOnly != null && episodeOnly != null ? seasonOnly + episodeOnly : null)
                              ?? seasonOnly
                              ?? episodeOnly;

                    if (seToken != null)
                    {
                        seToken = ZeroPadSeasonEpisode(seToken);
                        // Use the scene/Plex-preferred prefixed range, e.g. E01-12 -> E01-E12.
                        seToken = Regex.Replace(seToken, @"E(\d{2,3})-(\d{2,3})", "E$1-E$2");
                    }
                }

                var series = CleanSeriesName(head);
                if (!IsValidSeriesName(series))
                {
                    return false;
                }

                // Quality comes from the tail (after the year), plus any unambiguous source token misplaced in the
                // head/name before the year ("...India Special SatRip (2011)" -> source HDTV recovered).
                var quality = ExtractQuality(head, tail);

                var sb = new System.Text.StringBuilder(series);
                if (isTv && !string.IsNullOrEmpty(seToken))
                {
                    sb.Append(' ').Append(seToken);
                }

                if (year != null)
                {
                    sb.Append(" (").Append(year).Append(')');
                }

                if (quality.Length > 0)
                {
                    sb.Append(' ').Append(quality);
                }

                result = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
                // Collapse a degenerate single-episode range ("S03E01-E01" / "E05-E05") to a plain "S03E01" / "E05".
                result = Regex.Replace(result, @"E(\d{2,3})-E\1\b", "E$1");
                return true;
            }

            // Descriptors that are NOT part of a series name and must be stripped ("MythBusters some episodes").
            private static readonly Regex _descriptorNoiseRegex = new Regex(
                @"\b(?:some\s+episodes?|selected\s+episodes?|деякі\s+(?:епізоди|серії)|окремі\s+(?:епізоди|серії)|вибрані\s+(?:епізоди|серії))\b",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // A " / "-segment that is ONLY an episode/lecture-count descriptor (no real name): "Випуски 1-5",
            // "Лекції 1 - 24", "Курс лекцій", "Серії 1-20", "Episodes 1-10". Such a segment must never be picked as
            // the series name.
            private static readonly Regex _episodeDescriptorSegmentRegex = new Regex(
                @"^(?:(?:Випуск\w*|Серії|Серія|Серій|Лекці[їйя]|Лекцій|Епізоди?|Episodes?|Lectures?|Курс\s+лекцій|Усі\s+сері\w*|Всі\s+сері\w*)\s*[№#:]?\s*[\dXxХх.,\s-]*)+$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            private static bool HasLetter(string s) =>
                s != null && s.Any(c => (c < 128 && char.IsLetter(c)) || (c >= 0x0400 && c <= 0x04FF));

            // A series name is real when it has >=2 letters total (Latin or Cyrillic - non-consecutive is fine, so a
            // dotted acronym "K.O."/"S.W.A.T." counts), is a single ASCII-letter title ("X", "M"), or is a numeric
            // title (a pure number like "9"/"2067", or >=3 digits like "9-1-1"). A lone apostrophe/guillemet/symbol,
            // a single stray Latin letter glued to nothing, or a "2." fragment is NOT real - the caller then falls
            // back to rebuilding from the Cyrillic-kept title so a real (Cyrillic) name is preserved instead of junk.
            private static bool IsValidSeriesName(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return false;
                }

                var letters = name.Count(c => (c < 128 && char.IsLetter(c)) || (c >= 0x0400 && c <= 0x04FF));
                if (letters >= 2)
                {
                    return true;
                }

                if (Regex.IsMatch(name, @"^[A-Za-z]$"))
                {
                    return true;
                }

                // An alphanumeric short title (a Latin letter AND a digit: "F9", "M3", "R2") is real - the
                // season/episode/quality tokens are already removed before this check, so what remains is the name.
                if (Regex.IsMatch(name, "[A-Za-z]") && Regex.IsMatch(name, @"\d"))
                {
                    return true;
                }

                // A pure number ("9", "2067") or a multi-digit numeric title ("9-1-1") is a real title; a "2."
                // fragment (a single digit plus stray punctuation) is not.
                return Regex.IsMatch(name, @"^\d+$") || name.Count(char.IsDigit) >= 3;
            }

            private static string CleanSeriesName(string head)
            {
                // Drop season/episode tokens. The "of M" episode total is already removed from its episode token
                // upstream, so no blanket "of N" strip is done here - that would wrongly eat "of 1999"/"of 1" from a
                // real title like "Class Of 1999" or "The Story of 1".
                head = _seasonEpisodeTokenRegex.Replace(head, " ");

                // Remove bracket groups innermost-first so NESTED parentheses don't leave a stray ")" or "(" behind.
                var debracketed = head;
                string prev;
                do
                {
                    prev = debracketed;
                    debracketed = Regex.Replace(debracketed, @"\([^()]*\)|\[[^\[\]]*\]", " ");
                }
                while (debracketed != prev);

                // If removing the bracket groups deleted the ONLY letters, the title itself lived inside the brackets:
                // a stylized name ("[Rec]2") or a Latin alias left in parens once the Cyrillic main name was stripped
                // (" (Oh, Paris!)"). In that case UNWRAP (keep the contents) instead of dropping them. Otherwise drop
                // the groups plus any unbalanced stray bracket left over.
                head = HasLetter(head) && !HasLetter(debracketed)
                    ? Regex.Replace(head, @"[()\[\]]", " ")
                    : Regex.Replace(debracketed, @"[()\[\]]", " ");

                // Drop double-quote characters: a quoted Cyrillic segment stripped to empty leaves junk (' " " '),
                // and for a real title the quotes add nothing for *arr matching ("Altitude \"October Palace\"" ->
                // "Altitude October Palace"). A single apostrophe is kept (part of names like "Surf's Up"), but a
                // DOUBLED '' (uploaders' ASCII stand-in for « » around a concert/song name) is quote punctuation.
                head = Regex.Replace(head, "[\"«»“”]|''+", " ");

                // Drop non-name descriptors ("some episodes" / "деякі епізоди").
                head = _descriptorNoiseRegex.Replace(head, " ");

                // Drop a misplaced source/resolution token sitting in the NAME, before the year ("Top Gear. India
                // Special SatRip" -> "Top Gear. India Special"). ExtractQuality reads it from the head too, so the
                // source is not lost - it moves to the quality block where Sonarr/Radarr expect it. Only the
                // unambiguous source-anchor set is removed (never colour/codec tags that can be real title words).
                head = _sourceAnchorRegex.Replace(head, " ");

                // Pick the alternative title segment (Toloka separates them with " / ", sometimes " \ " or " | ").
                // Prefer a segment that has Latin letters (the real English/romaji title); among those, prefer the
                // highest Latin-minus-Cyrillic balance and the fewest Cyrillic letters, so a clean "2067" or "X"
                // beats a Cyrillic-laden twin and "Joy Ride" beats "Check-in у халепу". On a tie keep the LATER
                // segment (Toloka lists the localized name first, the original last - the original is what *arr
                // wants). When no segment has Latin, this still keeps the cleanest (numeric/Cyrillic) segment rather
                // than the " / "-joined duplicate. The space-padded split leaves an in-title slash ("Face/Off") and
                // the dotted "S.W.A.T." untouched, and the audio "|" in the tail is unaffected.
                var segments = Regex.Split(head, @"\s+[/\\|]\s+");
                var candidates = segments
                    .Select((s, i) => new { Segment = s, Latin = CountLatinLetters(s), Cyr = CountCyrillicLetters(s), Index = i })
                    .Where(x => x.Segment.Any(char.IsLetterOrDigit))
                    .ToList();

                // A segment that is ONLY an episode/lecture descriptor ("Випуски 1-5", "Лекції 1 - 24", "Курс
                // лекцій") is NOT the series name - exclude it from the pick so the real title segment wins. Keep
                // them only if every segment is such a descriptor (so a fallback name still exists).
                var titleCandidates = candidates.Where(x => !_episodeDescriptorSegmentRegex.IsMatch(x.Segment.Trim())).ToList();
                if (titleCandidates.Count > 0)
                {
                    candidates = titleCandidates;
                }

                var pick = candidates
                    .OrderByDescending(x => x.Latin > 0)
                    .ThenByDescending(x => x.Latin - x.Cyr)
                    .ThenBy(x => x.Cyr)
                    .ThenByDescending(x => x.Index)
                    .Select(x => x.Segment)
                    .FirstOrDefault()
                    ?? head;

                // Drop trailing pack/collection descriptors ("... Collection", "All seasons + movie") for clean lookup.
                pick = _collectionDescriptorRegex.Replace(pick, string.Empty);

                pick = Regex.Replace(pick, @"\s+", " ");
                // Remove a space left before a comma/period by a dropped parenthetical ("Friends , випуск" -> "Friends, випуск").
                pick = Regex.Replace(pick, @"\s+([,.])", "$1");
                return pick.Trim(_TitleTrimChars);
            }

            private static int CountLatinLetters(string value)
            {
                var count = 0;
                foreach (var ch in value)
                {
                    if (ch < 128 && char.IsLetter(ch))
                    {
                        count++;
                    }
                }

                return count;
            }

            private static int CountCyrillicLetters(string value)
            {
                var count = 0;
                foreach (var ch in value)
                {
                    if (ch >= 0x0400 && ch <= 0x04FF)
                    {
                        count++;
                    }
                }

                return count;
            }

            private static string ExtractQuality(string head, string tail)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var tokens = new List<string>();

                // A source token can sit in the NAME, before the year ("...India Special SatRip (2011)"). Scan the
                // head only with the unambiguous source-anchor set so a real title word (a movie named "Hybrid" /
                // "Remux") is never mistaken for a quality token.
                foreach (Match m in _sourceAnchorRegex.Matches(head))
                {
                    var token = NormalizeQualityToken(m.Value);
                    if (seen.Add(token))
                    {
                        tokens.Add(token);
                    }
                }

                foreach (Match m in _qualityTokenRegex.Matches(tail))
                {
                    var token = NormalizeQualityToken(m.Value);
                    if (seen.Add(token))
                    {
                        tokens.Add(token);
                    }
                }

                // A "BluRay Remux" / "DVD Remux" already implies the base source, so drop a redundant standalone
                // "BluRay" / "DVD" that a second token produced ("Blu-Ray BDRemux" -> "BluRay BluRay Remux").
                if (tokens.Contains("BluRay Remux"))
                {
                    tokens.Remove("BluRay");
                }

                if (tokens.Contains("DVD Remux"))
                {
                    tokens.Remove("DVD");
                }

                return string.Join(" ", tokens);
            }

            // Normalizes a Toloka quality token to the canonical token Sonarr/Radarr actually parse. Toloka uses many
            // custom/non-standard source names; the targets are verified against the live Sonarr+Radarr QualityParser
            // regexes (see for_testing/format_lang_standardization). A source word only sets the bucket when a
            // resolution token sits beside it, so these never invent a resolution.
            private static string NormalizeQualityToken(string token)
            {
                // Fold spacing/hyphens so "BD Rip", "BDRip" and "WEB-DL" share one switch key.
                var key = token.ToUpperInvariant().Replace(" ", string.Empty).Replace("-", string.Empty);
                switch (key)
                {
                    case "4K":
                        return "2160p";
                    case "2K":
                        return "1440p";

                    // Blu-ray family -> the one-word "BluRay" (Sonarr does NOT parse spaced "Blu Ray"); a rip/encode
                    // shares the Bluray bucket with a full disc.
                    case "BLURAY":
                    case "BDRIP":
                    case "BRRIP":
                    case "HDDVDRIP":
                        return "BluRay";

                    // Remux -> emit the BluRay source AND the literal "Remux" word: Sonarr needs both (+ a 1080p/2160p
                    // resolution) to score the Remux tier; the glued "BDRemux" alone yields no source at all.
                    case "BDREMUX":
                    case "REMUX":
                        return "BluRay Remux";

                    // Web re-encodes -> WEBRip (MediaInfo-confirmed; raw "SiteRip" parses as nothing).
                    case "WEBDLRIP":
                    case "SITERIP":
                    case "WEBRIP":
                        return "WEBRip";
                    case "WEBDL":
                        return "WEB-DL";

                    // Digital/broadcast captures (incl. transport-stream remuxes) -> HDTV; the resolution token
                    // carries SD vs HD.
                    case "HDTV":
                    case "HDTVRIP":
                    case "DVBRIP":
                    case "DVBREMUX":
                    case "IPTVRIP":
                    case "IPTVREMUX":
                    case "IPTV":
                    case "SATRIP":
                    case "SATREMUX":
                    case "HDRIP":
                    case "DSRIP":
                    case "DTVRIP":
                    case "UHDTVRIP": // HD broadcast capture mislabelled "UHD" - it is HDTV, not 2160p.
                    case "HDTRIP":   // typo of HDTVRip.
                    case "HDV":      // HD video tape (1080i).
                        return "HDTV";
                    case "PDTVRIP":
                        return "PDTV";

                    // Analog/low-grade captures -> SDTV.
                    case "VHSRIP":
                    case "LDRIP":
                    case "VCDRIP":
                        return "SDTV";

                    // A WEB-DL re-encode typo'd as "WBDLRip" -> WEBRip (same bucket as WEB-DLRip).
                    case "WBDLRIP":
                        return "WEBRip";

                    // DVD family -> DVD. A DVD-Remux keeps the literal "Remux" word (like BDRemux) so the lossless
                    // tier is not silently dropped, even though *arr has no dedicated DVD-Remux quality bucket.
                    case "DVD5":
                    case "DVD9":
                    case "DVD":
                    case "DVDUPSCALE":
                        return "DVD";
                    case "DVDREMUX":
                        return "DVD Remux";

                    case "3D":
                        return "3D";

                    case "CAMRIP":
                        return "CAM";

                    case "X264":
                        return "x264";
                    case "X265":
                        return "x265";

                    // HDR/colour markers (inert for the base quality, used by *arr custom formats) - normalize+preserve.
                    case "DV":
                    case "DOVI":
                    case "DOLBYVISION":
                        return "DV";
                    case "HDR10+":
                        return "HDR10+";
                    case "HDR10":
                        return "HDR10";
                    case "HDR":
                        return "HDR";
                    case "HLG":
                        return "HLG";
                    case "SDR":
                        return "SDR";
                }

                if (Regex.IsMatch(token, @"^\d+[pi]$", RegexOptions.IgnoreCase))
                {
                    return token.ToLowerInvariant();
                }

                // Multi-disc DVD descriptors ("16xDVD9", "DVD9+DVD5") -> the DVD source bucket.
                if (Regex.IsMatch(key, @"DVD[59]"))
                {
                    return "DVD";
                }

                return token;
            }

            // ASCII punctuation that is part of real uploader/studio handles (e.g. "HaKer_256", "Seto.Haruki",
            // "Otaku-First", "Marco Polo", "Gwean_&_Maslinka"). Preserved verbatim so the name still matches the
            // community's Sonarr/Radarr custom formats, which key on the exact handle.
            private static readonly HashSet<char> _releaseGroupPunctuation = new HashSet<char> { '_', '.', '-', ' ', '&' };

            // Builds a Sonarr/Radarr-friendly release-group token from a Toloka uploader/author name. ASCII handles
            // are preserved verbatim (case + "_.-& " separators) so they match the community custom formats keyed on
            // the exact handle; genuinely Cyrillic names are transliterated to Latin. Returns null when unusable.
            public static string SanitizeReleaseGroup(string author)
            {
                if (string.IsNullOrWhiteSpace(author))
                {
                    return null;
                }

                author = author.Trim();
                if (author.Equals("Anonymous", StringComparison.OrdinalIgnoreCase) || author.Equals("Анонім", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                // Fix Cyrillic homoglyphs hidden in an otherwise-Latin handle first (e.g. "Аlех" -> "Alex"), so the
                // transliteration step below doesn't phonetically mangle them ("х" -> "kh" would yield "Alekh").
                author = NormalizeNameHomoglyphs(author);

                var sb = new System.Text.StringBuilder();
                foreach (var ch in author)
                {
                    if (ch < 128 && (char.IsLetterOrDigit(ch) || _releaseGroupPunctuation.Contains(ch)))
                    {
                        // ASCII letter/digit or a handle separator: keep verbatim (preserves case + "HaKer_256" form).
                        sb.Append(ch);
                    }
                    else if (_translit.TryGetValue(char.ToLowerInvariant(ch), out var mapped))
                    {
                        // Genuinely Cyrillic: transliterate, preserving the capitalization of the first letter.
                        sb.Append(char.IsUpper(ch) && mapped.Length > 0 ? char.ToUpperInvariant(mapped[0]) + mapped.Substring(1) : mapped);
                    }

                    // anything else (other scripts, exotic punctuation) is dropped
                }

                // Collapse any double spaces introduced by dropped characters and trim edge whitespace.
                var result = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
                return result.Length >= 2 && result.Any(char.IsLetterOrDigit) ? result : null;
            }

            private static readonly Dictionary<char, string> _translit = new Dictionary<char, string>
            {
                ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "h", ['ґ'] = "g", ['д'] = "d", ['е'] = "e", ['є'] = "ie",
                ['ж'] = "zh", ['з'] = "z", ['и'] = "y", ['і'] = "i", ['ї'] = "i", ['й'] = "i", ['к'] = "k", ['л'] = "l",
                ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u",
                ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "shch", ['ь'] = "", ['ю'] = "iu",
                ['я'] = "ia", ['ё'] = "e", ['ы'] = "y", ['э'] = "e", ['ъ'] = ""
            };

            private static readonly char[] _TitleTrimChars = { ' ', '&', ',', '.', '!', '?', '+', '-', '_', '|', '/', '\\', ':', ';', 'ʼ', '`' };

            // If the title ends with a bracketed/parenthesised tag that already appears earlier, remove the trailing copy.
            private static string RemoveDuplicateTrailingTag(string input)
            {
                foreach (var findTagsRegex in _FindTagsInTitlesRegexList)
                {
                    var matches = findTagsRegex.Matches(input);
                    if (matches.Count < 2)
                    {
                        continue;
                    }

                    var last = matches[matches.Count - 1];

                    // Only consider it trailing when nothing meaningful follows the last tag.
                    if (input.Substring(last.Index + last.Length).Trim(_TitleTrimChars).Length != 0)
                    {
                        continue;
                    }

                    for (var i = 0; i < matches.Count - 1; i++)
                    {
                        if (string.Equals(matches[i].Value, last.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            input = input.Remove(last.Index, last.Length).Trim();
                            break;
                        }
                    }
                }

                return input;
            }

            private static string MoveFirstTagsToEndOfReleaseTitle(string input)
            {
                var output = input.Trim(_TitleTrimChars);
                foreach (var findTagsRegex in _FindTagsInTitlesRegexList)
                {
                    var expectedIndex = 0;
                    foreach (Match match in findTagsRegex.Matches(output))
                    {
                        if (match.Index > expectedIndex)
                        {
                            var substring = output.Substring(expectedIndex, match.Index - expectedIndex);
                            if (string.IsNullOrWhiteSpace(substring))
                            {
                                expectedIndex = match.Index;
                            }
                            else
                            {
                                break;
                            }
                        }

                        // Remove the first literal occurrence of the tag (no regex needed) and re-append it.
                        var tag = match.ToString();
                        var idx = output.IndexOf(tag, StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            output = $"{output.Remove(idx, tag.Length)} {tag}".Trim();
                        }

                        expectedIndex += tag.Length;
                    }
                }

                return output.Trim();
            }
        }
    }
}
