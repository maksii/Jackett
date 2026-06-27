using System.Collections;
using System.Collections.Generic;
using Jackett.Common.Models;
using NUnit.Framework;
using TolokaIndexer = Jackett.Common.Indexers.Definitions.Toloka;

namespace Jackett.Test.Common.Indexers.Toloka
{
    [TestFixture]
    public class TolokaTests
    {
        [TestCaseSource(typeof(TitleParserTestData), nameof(TitleParserTestData.TestCases))]
        public string TestTitleParsing(string title, ICollection<int> category, bool stripCyrillicLetters)
        {
            var titleParser = new TolokaIndexer.TitleParser();

            return titleParser.Parse(title, category, stripCyrillicLetters);
        }

        [TestCase("FanVoxUA", ExpectedResult = "FanVoxUA")]
        // A transliterated multi-word Cyrillic name is also underscore-joined (no spaces in a release group).
        [TestCase("Сталь Кується", ExpectedResult = "Stal_Kuietsia")]
        // Anonymous uploads still get an explicit "Anonymous" group (both the Latin and Cyrillic markers).
        [TestCase("Anonymous", ExpectedResult = "Anonymous")]
        [TestCase("Анонім", ExpectedResult = "Anonymous")]
        [TestCase("", ExpectedResult = null)]
        [TestCase("   ", ExpectedResult = null)]
        // A multi-word handle has its spaces replaced with underscores (a release group token cannot contain spaces).
        [TestCase("UkrDub Team", ExpectedResult = "UkrDub_Team")]
        [TestCase("Ukr Voice Team", ExpectedResult = "Ukr_Voice_Team")]
        [TestCase("Marco Polo", ExpectedResult = "Marco_Polo")]
        // ASCII handles keep their non-space separators/case verbatim so they match the community custom formats.
        [TestCase("HaKer_256", ExpectedResult = "HaKer_256")]
        [TestCase("Romario_O", ExpectedResult = "Romario_O")]
        [TestCase("Seto.Haruki", ExpectedResult = "Seto.Haruki")]
        [TestCase("Otaku-First", ExpectedResult = "Otaku-First")]
        [TestCase("Gwean_&_Maslinka", ExpectedResult = "Gwean_&_Maslinka")]
        // Latin handles with hidden Cyrillic homoglyphs: look-alike-normalized, NOT phonetically transliterated
        // ("х" would otherwise become "kh" -> "Alekh"; "а" stays "a").
        [TestCase("wаrden", ExpectedResult = "warden")]
        [TestCase("Аlех", ExpectedResult = "Alex")]
        // All-Cyrillic name is transliterated (НТН -> NTN, not the homoglyph garbage HTH).
        [TestCase("НТН", ExpectedResult = "NTN")]
        public string TestSanitizeReleaseGroup(string author)
        {
            return TolokaIndexer.TitleParser.SanitizeReleaseGroup(author);
        }

        // A username carrying hidden Cyrillic look-alikes is normalized to Latin (so uploader search matches the real
        // account); a genuinely Cyrillic handle is left untouched for the caller to transliterate.
        [TestCase("wаrden", ExpectedResult = "warden")]
        [TestCase("Аlех", ExpectedResult = "Alex")]
        [TestCase("warden", ExpectedResult = "warden")]
        [TestCase("Гуртом", ExpectedResult = "Гуртом")]
        // All-Cyrillic acronym whose every letter has a Latin look-alike: must stay Cyrillic (it's a real word,
        // not a disguised Latin one), so it transliterates to "NTN" downstream rather than the garbage "HTH".
        [TestCase("НТН", ExpectedResult = "НТН")]
        public string TestNormalizeNameHomoglyphs(string name)
        {
            return TolokaIndexer.TitleParser.NormalizeNameHomoglyphs(name);
        }

        [Test]
        public void TestAppendsReleaseGroupForSonarr()
        {
            var parser = new TolokaIndexer.TitleParser();
            var group = TolokaIndexer.TitleParser.SanitizeReleaseGroup("FanVoxUA");

            var result = parser.Parse(
                "Магічна битва (Сезон 3) / Jujutsu Kaisen (Season 3) (2026) WEBDLRip 1080p H.265",
                new List<int> { TorznabCatType.TVAnime.ID },
                true,
                group);

            // Season tag zero-padded and converted, group appended for Sonarr release-group detection.
            Assert.That(result, Does.Contain("S03"));
            Assert.That(result, Does.EndWith("-FanVoxUA"));
        }

        [Test]
        public void TestExactRangesPreservesDisjointEpisodes()
        {
            var parser = new TolokaIndexer.TitleParser();

            var result = parser.Parse(
                "Бліч / Bleach (серії 110-127, 138-167, 190-203, 215-226, 266-286, 288-293 з 366) (2004-2012) BDRip 1080p",
                new List<int> { TorznabCatType.TVAnime.ID },
                true,
                null,
                null,
                exactRanges: true);

            Assert.That(result, Is.EqualTo("Bleach E110-E127, E138-E167, E190-E203, E215-E226, E266-E286, E288-E293 (2004-2012) BluRay 1080p"));
        }

        [Test]
        public void TestRangeModeCollapsesDisjointEpisodes()
        {
            var parser = new TolokaIndexer.TitleParser();

            // Default (range) mode: same input collapses to the min-max envelope.
            var result = parser.Parse(
                "Бліч / Bleach (серії 110-127, 138-167, 190-203, 215-226, 266-286, 288-293 з 366) (2004-2012) BDRip 1080p",
                new List<int> { TorznabCatType.TVAnime.ID },
                true,
                null,
                null,
                exactRanges: false);

            Assert.That(result, Is.EqualTo("Bleach E110-E293 (2004-2012) BluRay 1080p"));
        }

        [Test]
        public void TestExactRangesPreservesDisjointSeasons()
        {
            var parser = new TolokaIndexer.TitleParser();

            var result = parser.Parse(
                "Молодий Морс / Endeavour (Сезони 1-6, 8-9) (2012-2023) BDRip-AVC Ukr/Eng | Sub Ukr",
                new List<int> { TorznabCatType.TV.ID },
                true,
                null,
                true,
                exactRanges: true);

            Assert.That(result, Is.EqualTo("Endeavour S01-S06, S08-S09 (2012-2023) BluRay x264 Ukrainian"));
        }

        [Test]
        public void TestExactRangesPreservesDisjointEpisodesWithinSeason()
        {
            var parser = new TolokaIndexer.TitleParser();

            var result = parser.Parse(
                "Зла наука / Wicked Science (Сезон 1, серії 1-8, 11-26) (2004) DVDRip Ukr/Eng | Sub Eng",
                new List<int> { TorznabCatType.TV.ID },
                true,
                null,
                true,
                exactRanges: true);

            Assert.That(result, Is.EqualTo("Wicked Science S01E01-E08, S01E11-E26 (2004) DVDRip Ukrainian"));
        }

        [Test]
        public void TestExactRangesKeepsEnvelopeWhenContiguous()
        {
            var parser = new TolokaIndexer.TitleParser();

            // No gap (1-12 then 13-24 are contiguous) -> exact mode still emits the clean envelope.
            var result = parser.Parse(
                "Бліч / Bleach (серії 1-12, 13-24) (2004) BDRip 1080p",
                new List<int> { TorznabCatType.TVAnime.ID },
                true,
                null,
                null,
                exactRanges: true);

            Assert.That(result, Is.EqualTo("Bleach E01-E24 (2004) BluRay 1080p"));
        }

        [Test]
        public void TestForumConventionTagsUkrainianWhenNoAudioToken()
        {
            var parser = new TolokaIndexer.TitleParser();

            // Dub-tree forum, title carries no explicit audio token -> convention supplies Ukrainian.
            var result = parser.Parse(
                "Коли я переродився слизом / Tensei shitara Slime Datta Ken (Season 4) (2026) WEB-DL 1080p",
                new List<int> { TorznabCatType.TVAnime.ID },
                true,
                null,
                ukrainianAudioDefault: true);

            Assert.That(result, Does.EndWith("Ukrainian"));
        }

        [Test]
        public void TestSubtitleForumWithNoAudioTokenIsNotTaggedUkrainian()
        {
            var parser = new TolokaIndexer.TitleParser();

            // Subtitle-tree forum, no explicit audio token -> original audio, must NOT be tagged Ukrainian.
            var result = parser.Parse(
                "Коли я переродився слизом / Tensei shitara Slime Datta Ken (Season 4) (2026) WEB-DL 1080p",
                new List<int> { TorznabCatType.TVAnime.ID },
                true,
                null,
                ukrainianAudioDefault: false);

            Assert.That(result, Does.Not.Contain("Ukrainian"));
        }

        [Test]
        public void TestUkrainianAudioWithEnglishSubsIsTaggedUkrainian()
        {
            var parser = new TolokaIndexer.TitleParser();

            // Ukrainian audio present, English subtitles: audio wins -> Ukrainian (subs are ignored for tagging).
            var result = parser.Parse(
                "Дюна / Dune (2024) WEB-DL 1080p Ukr/Jap | Sub Eng",
                new List<int> { TorznabCatType.Movies.ID },
                true,
                null,
                ukrainianAudioDefault: true);

            Assert.That(result, Is.EqualTo("Dune (2024) WEB-DL 1080p Ukrainian"));
        }

        [Test]
        public void TestNormalizeQualityOnNormalizesSourceTokens()
        {
            var parser = new TolokaIndexer.TitleParser();

            // Default (toggle on): Toloka's "BDRemux" is mapped to the canonical "BluRay Remux" tier.
            var result = parser.Parse(
                "Дюна / Dune (2021) BDRemux 1080p H.264 Ukr/Eng | Sub Ukr",
                new List<int> { TorznabCatType.Movies.ID },
                true, null, null, exactRanges: false, normalizeQuality: true);

            Assert.That(result, Is.EqualTo("Dune (2021) BluRay Remux 1080p x264 Ukrainian"));
        }

        [Test]
        public void TestNormalizeQualityOffKeepsOriginalSourceTokens()
        {
            var parser = new TolokaIndexer.TitleParser();

            // Toggle off: keep Toloka's original source token ("BDRemux"); resolution + codec are still normalized.
            var result = parser.Parse(
                "Дюна / Dune (2021) BDRemux 1080p H.264 Ukr/Eng | Sub Ukr",
                new List<int> { TorznabCatType.Movies.ID },
                true, null, null, exactRanges: false, normalizeQuality: false);

            Assert.That(result, Is.EqualTo("Dune (2021) BDRemux 1080p x264 Ukrainian"));
        }

        [Test]
        public void TestArchiveVideoMixedCategoryReconstructsTvSeason()
        {
            var parser = new TolokaIndexer.TitleParser();

            // Archive video (forum 72) and unformatted video (45) map to BOTH Movies and TV. A TV title in that mix
            // must still be reconstructed with its season token (it would be passed through verbatim under "Other").
            var result = parser.Parse(
                "Дім Давида (Сезон 2) / House of David (Season 2) WEB-DL 1080p Ukr/Eng",
                new List<int> { TorznabCatType.Movies.ID, TorznabCatType.TV.ID },
                true);

            Assert.That(result, Does.StartWith("House of David S02"));
            Assert.That(result, Does.Contain("WEB-DL 1080p"));
        }

        [Test]
        public void TestParseDetailsPage()
        {
            const string html = @"
<html>
<head>
    <link rel=""image_src"" href=""https://thumb.hurtom.com/image/w250/toloka.to/photos/sample_f0_0.jpg"" />
</head>
<body>
    <a class=""maintitle"" href=""t693540"">Sample Movie (2026)</a>
    <a href=""https://www.imdb.com/title/tt12343534/"">IMDb</a>
    <a href=""magnet:?xt=urn:btih:6f93077a2377edf06d67654abc1e1057c12e19d7&amp;dn=Sample"">magnet</a>
    <a href=""download.php?id=708258"">Завантажити</a>
</body>
</html>";

            var meta = TolokaIndexer.ParseDetailsPage(html);

            Assert.That(meta.Imdb, Is.EqualTo(12343534));
            Assert.That(meta.InfoHash, Is.EqualTo("6f93077a2377edf06d67654abc1e1057c12e19d7"));
            Assert.That(meta.MagnetUri, Is.Not.Null);
            Assert.That(meta.MagnetUri.ToString(), Does.StartWith("magnet:?xt=urn:btih:6f93077a"));
            Assert.That(meta.Poster, Is.Not.Null);
            Assert.That(meta.Poster.ToString(), Is.EqualTo("https://thumb.hurtom.com/image/w250/toloka.to/photos/sample_f0_0.jpg"));
        }

        [Test]
        public void TestParseDetailsPageRecoversResolutionFromFrameSize()
        {
            const string html = @"
<html><body>
    <a class=""maintitle"" href=""t1"">Sample (1996)</a>
    <span>Відео: кодек: H.264 розмір кадру: 1024 х 576 бітрейт: 1800 кб/с</span>
</body></html>";

            var meta = TolokaIndexer.ParseDetailsPage(html);

            // 1024x576 -> 576p (width 1000-1259 band).
            Assert.That(meta.Resolution, Is.EqualTo("576p"));
        }

        [Test]
        public void TestParseDetailsPageWithoutMetadata()
        {
            var meta = TolokaIndexer.ParseDetailsPage("<html><body>nothing here</body></html>");

            Assert.That(meta.Imdb, Is.Null);
            Assert.That(meta.InfoHash, Is.Null);
            Assert.That(meta.MagnetUri, Is.Null);
            Assert.That(meta.Poster, Is.Null);
        }

        [Test]
        public void TestParseGrabCountsFromApiJson()
        {
            // The api.php search returns the completed/grabs count the HTML page hides ("complete").
            const string json = @"[
  { ""id"": ""695553"", ""title"": ""Slime S4"", ""seeders"": ""22"", ""complete"": ""117"" },
  { ""id"": ""678039"", ""title"": ""Slime S3"", ""seeders"": ""20"", ""complete"": ""1245"" }
]";
            var grabs = TolokaIndexer.ParseGrabCounts(json);

            Assert.That(grabs["695553"], Is.EqualTo(117));
            Assert.That(grabs["678039"], Is.EqualTo(1245));
        }

        [Test]
        public void TestParseGrabCountsTolerantOfNonJsonBody()
        {
            // The api returns plain text on error/empty - must yield no counts rather than throw.
            Assert.That(TolokaIndexer.ParseGrabCounts(""), Is.Empty);
            Assert.That(TolokaIndexer.ParseGrabCounts("Nothing found"), Is.Empty);
        }
    }

    public class TitleParserTestData
    {
        public static IEnumerable TestCases
        {
            get
            {
                // Stripped Cyrillic (default): reconstructed to the canonical Sonarr shape
                // "Series SxxExx (Year) Source Resolution Codec" - SxxExx right after the title, single Latin
                // title, prefixed episode range, language/subtitle clutter dropped.
                yield return new TestCaseData("Правдива терапія (Сезон 1, серії 1-2) / Shrinking (Season 1, episodes 1-2) (2023) WEBRip 1080p Ukr/Eng", new List<int> { TorznabCatType.TV.ID }, true).Returns("Shrinking S01E01-E02 (2023) WEBRip 1080p Ukrainian");
                yield return new TestCaseData("Ші-Ра та принцеси могутності (сезон 1-2, серій 14 з 20) / She-Ra and the Princesses of Power (seasons 1-2, episodes 14 of 20) (2018) WEBRip 1080p", new List<int> { TorznabCatType.TVHD.ID }, true).Returns("She-Ra and the Princesses of Power S01-02E01-E14 (2018) WEBRip 1080p");
                yield return new TestCaseData("А інші сгорять у пеклі (Сезон 1, Серія 3) / Everyone Else Burns (Season 1, Episode 3) (2023) WEB-DL 1080p Ukr/Eng | Sub Ukr/Eng", new List<int> { TorznabCatType.TVOther.ID }, true).Returns("Everyone Else Burns S01E03 (2023) WEB-DL 1080p Ukrainian");
                // Original (English) audio + Ukrainian subs: the audio language is English, so it must NOT be tagged Ukrainian.
                yield return new TestCaseData("У тілі (Сезон 2, Епізод 1,2 з ХХ) / In the flesh (Season 2, episodes 1,2 of XX) (2014) 1080p BDRip Eng | sub Ukr", new List<int> { TorznabCatType.TV.ID }, true).Returns("In the flesh S02E01-E02 (2014) 1080p BluRay English");
                // Sport is passed through unchanged (pipe-delimited event listings are not Sonarr/Radarr-matchable).
                yield return new TestCaseData("Формула-1 | Сезон 2024 | Етап 01 | Setanta Sports (01-02.03.2024) WEB-DL 1080p Ukr", new List<int> { TorznabCatType.TVSport.ID }, true).Returns("Формула-1 | Сезон 2024 | Етап 01 | Setanta Sports (01-02.03.2024) WEB-DL 1080p Ukr");

                // Cyrillic kept: both localized and original titles legitimately carry the tag, nothing is dropped.
                yield return new TestCaseData("Правдива терапія (Сезон 1, серії 1-2) / Shrinking (Season 1, episodes 1-2) (2023) WEBRip 1080p Ukr/Eng", new List<int> { TorznabCatType.TVHD.ID }, false).Returns("Правдива терапія (S01E01-02) / Shrinking (S01E01-02) (2023) WEBRip 1080p Ukr/Eng");
                yield return new TestCaseData("Ші-Ра та принцеси могутності (сезон 1-2, серій 14 з 20) / She-Ra and the Princesses of Power (seasons 1-2, episodes 14 of 20) (2018) WEBRip 1080p", new List<int> { TorznabCatType.TVAnime.ID }, false).Returns("Ші-Ра та принцеси могутності (S01-S02, E01-14) / She-Ra and the Princesses of Power (S01-S02, E01-14) (2018) WEBRip 1080p");
                yield return new TestCaseData("А інші сгорять у пеклі (Сезон 1, Серія 3) / Everyone Else Burns (Season 1, Episode 3) (2023) WEB-DL 1080p Ukr/Eng | Sub Ukr/Eng", new List<int> { TorznabCatType.TVDocumentary.ID }, false).Returns("А інші сгорять у пеклі (S01E03) / Everyone Else Burns (S01E03) (2023) WEB-DL 1080p Ukr/Eng | Sub Ukr/Eng");
                yield return new TestCaseData("У тілі (Сезон 2, Епізод 1,2 з ХХ) / In the flesh (Season 2, episodes 1,2 of XX) (2014) 1080p BDRip Eng | sub Ukr", new List<int> { TorznabCatType.TV.ID }, false).Returns("У тілі (S02E01-02) / In the flesh (S02E01-02) (2014) 1080p BDRip Eng | sub Ukr");

                // Anime season pack (TV): season retained as S03, WEBDLRip -> WEB-DL, H.265 -> x265.
                yield return new TestCaseData("Магічна битва (Сезон 3) / Jujutsu Kaisen (Season 3) (2026) WEBDLRip 1080p H.265 Ukr/Jap | sub Ukr", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Jujutsu Kaisen S03 (2026) WEBRip 1080p x265 Ukrainian");

                // Genuine movie (Radarr): reconstructed to "Title (Year) Source Resolution Codec"; H.264 -> x264; Ukr audio tagged.
                yield return new TestCaseData("Дюна / Dune (2021) BDRip 1080p H.264 Ukr/Eng | Sub Ukr", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Dune (2021) BluRay 1080p x264 Ukrainian");

                // Cyrillic homoglyphs hidden in Latin tech tokens ("BDRір" with Cyrillic і/р, "1080р" with Cyrillic р)
                // must be normalised so resolution/source/codec survive instead of being stripped away.
                yield return new TestCaseData("Дюна / Dune (2021) BDRір 1080р H.264 Ukr/Eng", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Dune (2021) BluRay 1080p x264 Ukrainian");

                // Edition markers (Director's Cut) are preserved for Radarr edition/upgrade matching.
                yield return new TestCaseData("Джон Кеннеді / JFK [Director's Cut] (1991) BDRip H.264 Ukr/Eng", new List<int> { TorznabCatType.Movies.ID }, true).Returns("JFK (1991) BluRay x264 Director's Cut Ukrainian");

                // PROPER tag is preserved (Sonarr/Radarr use it for upgrade decisions).
                yield return new TestCaseData("Дюна / Dune (2021) PROPER BDRip 1080p H.264 Ukr", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Dune (2021) BluRay 1080p x264 PROPER Ukrainian");

                // Multi-season pack: Sonarr-preferred "S01-S03" form.
                yield return new TestCaseData("Воїн / Warrior (Season 1-3) (2019-2023) WEB-DL AVC", new List<int> { TorznabCatType.TV.ID }, true).Returns("Warrior S01-S03 (2019-2023) WEB-DL x264");

                // Disjoint multi-range episode list collapses to a first-last envelope (no truncation to the first range).
                yield return new TestCaseData("Бліч / Bleach (серії 110-127, 138-167, 190-203, 215-226, 266-286, 288-293 з 366) (2004-2012) BDRip 1080p", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Bleach E110-E293 (2004-2012) BluRay 1080p");

                // "Part"/"Частина" markers are kept so distinct parts of a season don't collide on the same title.
                yield return new TestCaseData("Життя з нуля / Re:Zero (Сезон 4, частина 1) (2026) WEB-DL 1080p", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Re:Zero S04 (2026) WEB-DL 1080p Part 1");

                // Specials markers in the many real forms uploaders use (verified against live Toloka). Before, only the
                // bare plurals "спецепізоди"/"спецвипуски" and Latin OVA/ONA were matched; the common singulars, the
                // Cyrillic look-alike "ОВА", the transliterated "спешл" and "OAD" were silently dropped on rebuild.
                // Cyrillic "ОВА" -> "OVA" (90 live rows write OVA in Cyrillic); audio is Japanese here (subs Ukr).
                yield return new TestCaseData("Безтурботні часи (Сезон 1 + ОВА) / Non Non Biyori (Season 1 + OVA) (2013) BDRip 1080p H.265 Jap | sub Ukr", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Non Non Biyori S01 (2013) BluRay 1080p x265 OVA Japanese");
                // Transliterated "спешл" -> "Specials".
                yield return new TestCaseData("Робота Клітин (Сезон 1 + спешл) / Hataraku Saibou (Season 1) (2018) BDRip 1080p H.265 60fps Ukr/Jap | Sub Ukr", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Hataraku Saibou S01 (2018) BluRay 1080p x265 Specials Ukrainian");
                // "спецепізоди" -> "Specials".
                yield return new TestCaseData("Макросс 7 (Сезон 1 + спецепізоди) / Macross 7 (Season 1) (1994) BDRip 1080p H.265 Ukr/Jap | sub Ukr", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Macross 7 S01 (1994) BluRay 1080p x265 Specials Ukrainian");
                // "OAD" (anime Original Animation DVD) is preserved as its own tag, not collapsed to OVA.
                yield return new TestCaseData("Це провина не моя / WataMote! (Season 1 + OAD) (2013) WEB-DLRip 720p Ukr/Jap", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("WataMote S01 (2013) WEBRip 720p OAD Ukrainian");
                // "Спецсерії" -> "Specials"; the cross-season "302 серії" grand total is dropped (it is not
                // season-relative) so the multi-season pack collapses cleanly to "S01-S12" with a Specials tag.
                yield return new TestCaseData("Щенячий патруль (Сезон 1-12+Спецсерії, 302 серії) / PAW Patrol (Season 1-12) (2013-2026) WEB-DLRip 1080p H.265 Ukr/Eng | sub Eng", new List<int> { TorznabCatType.TV.ID }, true).Returns("PAW Patrol S01-S12 (2013-2026) WEBRip 1080p x265 Specials Ukrainian");

                // Series name with a Latin roman-numeral fragment left over from the stripped Cyrillic title: pick the
                // segment with the MOST Latin (the real name "Date a Live IV"), not the junk first fragment "IV".
                yield return new TestCaseData("Побачення з життям IV (Сезон 4) / Date a Live IV (Season 4) (2022) BDRip 1080р H.265 2xUkr/Jap | Sub Ukr/Eng", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Date a Live IV S04 (2022) BluRay 1080p x265 Ukrainian");

                // A Part marker that survives in the series name (before the year) must NOT be duplicated by the extras append.
                yield return new TestCaseData("Дюна: Частина 2 / Dune: Part 2 (2024) BDRip 1080p Ukr/Eng", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Dune: Part 2 (2024) BluRay 1080p Ukrainian");

                // Cyrillic-only video title (Japanese audio, no Latin name): the name is kept rather than reduced to a
                // title-less "quality salad" - the audio language is Japanese (subs Ukr), so it is tagged Japanese, not Ukrainian.
                yield return new TestCaseData("Замість тисячі слів - Збірка коротких метрів (2014-2021) WEBDLRip 1080p H.264 Jap | Sub Ukr", new List<int> { TorznabCatType.TV.ID }, true).Returns("Замість тисячі слів - Збірка коротких метрів (2014-2021) WEBRip 1080p x264 Japanese");

                // Multi-season pack with the Ukrainian PLURAL "Сезони" (was previously missed, losing the season range).
                yield return new TestCaseData("Офіс / The Office (Сезони 1-9) (2005-2013) WEB-DL 1080p Ukr/Eng | Sub Eng", new List<int> { TorznabCatType.TV.ID }, true).Returns("The Office S01-S09 (2005-2013) WEB-DL 1080p Ukrainian");

                // A title word that coincides with a language code ("Dan") must NOT be read as the audio language;
                // only tokens after the year count, so the real audio "Eng" wins -> English (not Danish).
                yield return new TestCaseData("Танець з Деном / Dance with Dan (2024) BDRip 1080p Eng | Sub Ukr", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Dance with Dan (2024) BluRay 1080p English");

                // Multi-digit multi-dub prefix ("10xUkr") must still resolve to Ukrainian audio.
                yield return new TestCaseData("Пеклорай / Jigokuraku (2023) BDRip 1080p H.265 10xUkr/Jap | sub Eng", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Jigokuraku (2023) BluRay 1080p x265 Ukrainian");

                // Non-standard / interlaced resolution tokens (540p, 480i) must be preserved, not dropped.
                yield return new TestCaseData("Клеопатра / Cleopatra (1999) DVDRemux 540p Ukr/Eng | Sub Eng", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Cleopatra (1999) DVD Remux 540p Ukrainian");
                yield return new TestCaseData("Атанаржуат / Atanarjuat: The Fast Runner (2001) DVDRemux 480i Ukr/Inu | Sub Ukr/Eng", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Atanarjuat: The Fast Runner (2001) DVD Remux 480i Ukrainian");

                // Non-standard sources preserved: Hybrid, UHD/HDR.
                yield return new TestCaseData("Яйце Янгола / Tenshi no Tamago (1985) UHD-BDRip 1080p HDR H.265 2xUkr/Jpn | Sub Jpn", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Tenshi no Tamago (1985) UHD BluRay 1080p HDR x265 Ukrainian");

                // "Ai Rem" (AI Remaster) edition preserved.
                yield return new TestCaseData("Провалений мудрець / Rakudai Kenja (Season 1) (2026) WEBRip Ai Rem 1080p H.265 Ukr/Jap | Sub Ukr", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Rakudai Kenja S01 (2026) WEBRip 1080p x265 AI Remastered Ukrainian");

                // Multi-year COLLECTION pack: year list collapses to a span and the title is properly reconstructed.
                yield return new TestCaseData("Планета Мавп. Колекція / Planet of the Apes. Collection (1968,1970,2001) BDRip-AVC Ukr/Eng | Sub Ukr/Eng", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Planet of the Apes (1968-2001) BluRay x264 Ukrainian");

                // Numeric/symbol-only title must not be doubled across the " / " split.
                yield return new TestCaseData("9-1-1 (Служба порятунку) (Сезони 1-8) / 9-1-1 (Season 1-8) (2018-2025) WEB-DLRip-AVC Ukr/Eng | Sub Eng", new List<int> { TorznabCatType.TV.ID }, true).Returns("9-1-1 S01-S08 (2018-2025) WEBRip x264 Ukrainian");

                // Comma-separated season list keeps all seasons (was dropping the second).
                yield return new TestCaseData("Дурні тести / Baka to Test (Season 1, 2) (2010-2011) BDRip 1080p H.265 Ukr/Jap | Sub Ukr", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Baka to Test S01-S02 (2010-2011) BluRay 1080p x265 Ukrainian");

                // Count form "Серій N з M" = N episodes present, not "episode N".
                yield return new TestCaseData("Магічна битва (Сезон 3, серій 12 з 23) / Jujutsu Kaisen (Season 3, episodes 12 of 23) (2026) WEB-DL 1080p Ukr/Jap | Sub Ukr", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Jujutsu Kaisen S03E01-E12 (2026) WEB-DL 1080p Ukrainian");

                // Nested parentheses must not leave a stray bracket in the series name.
                yield return new TestCaseData("Сабріна / Chilling Adventures of Sabrina (Parts 1-4 (Seasons 1-2)) (2018-2020) WEB-DLRip-AVC | Sub Ukr", new List<int> { TorznabCatType.TV.ID }, true).Returns("Chilling Adventures of Sabrina S01-S02 (2018-2020) WEBRip x264 Parts 1-4");

                // Unbalanced bracket left by a nested season/episode group must not survive in the series name. The
                // two-season pack (season 1 partial + season 2) collapses to the cross-season envelope S01-S02.
                yield return new TestCaseData("Зла наука (Сезон 1 (серії 1-8, 11-26), Сезон 2) / Wicked Science (Season 1 (episodes 1-8, 11-26), Season 2) (2004-2006) DVDRip-AVC Ukr/Eng | sub Eng", new List<int> { TorznabCatType.TV.ID }, true).Returns("Wicked Science S01-S02 (2004-2006) DVDRip x264 Ukrainian");

                // Pack/collection descriptor stripped from the series name so lookup resolves.
                yield return new TestCaseData("Пуститися берега. Всі сезони + фільм / Breaking Bad. All seasons + movie (2008-2013/2019) BDRip-AVC Ukr/Eng | Sub Ukr/Eng", new List<int> { TorznabCatType.TV.ID }, true).Returns("Breaking Bad (2008-2019) BluRay x264 Ukrainian");

                // Season RANGE + trailing episode count: the count must NOT become a season endpoint.
                yield return new TestCaseData("Малюки Луні Тюнз / Baby Looney Tunes (Сезон 1-2, 52 серії з 55) (2002) SATRip 720p", new List<int> { TorznabCatType.TV.ID }, true).Returns("Baby Looney Tunes S01-S02 (2002) HDTV 720p");
                // Single season + episode RANGE (keyword after).
                yield return new TestCaseData("Бургери Боба / Bob's Burgers (Сезон 1, 9-13 серії) (2011) WEB-DL 1080p", new List<int> { TorznabCatType.TV.ID }, true).Returns("Bob's Burgers S01E09-E13 (2011) WEB-DL 1080p");
                // Single season + episode COUNT -> expanded to the released episode range (count, not "episode N").
                yield return new TestCaseData("Елвін / Alvin and the Chipmunks (Сезон 3, 44 серій з 52) (2017) WEB-DL 1080p", new List<int> { TorznabCatType.TV.ID }, true).Returns("Alvin and the Chipmunks S03E01-E44 (2017) WEB-DL 1080p");
                // Yearless release: anchor on the source token, drop language clutter.
                yield return new TestCaseData("Дім Давида (Сезон 2) / House of David (Season 2) WEB-DL 1080p Ukr/Eng", new List<int> { TorznabCatType.TV.ID }, true).Returns("House of David S02 WEB-DL 1080p Ukrainian");
                // Per-segment repeated year: anchor on the LAST year so the Latin title survives.
                yield return new TestCaseData("Готель дель Луна (Сезон 1) (2019) / Hotel del Luna (Season 1) (2019) HDTVRip 1080p", new List<int> { TorznabCatType.TV.ID }, true).Returns("Hotel del Luna S01 (2019) HDTV 1080p");
                // Standalone "N-M серії" episode range with no season prefix; Cyrillic-only name preserved.
                yield return new TestCaseData("Гвардія (1-4 серії, з 12) (2015) WEB-DLRip 720p", new List<int> { TorznabCatType.TV.ID }, true).Returns("Гвардія E01-E04 (2015) WEBRip 720p");
                // "Of <number>" is part of a real MOVIE title and must NOT be stripped as an episode count.
                yield return new TestCaseData("Клас 1999го / Class Of 1999 (1990) BDRip-AVC Ukr/Eng | Sub Ukr/Eng", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Class Of 1999 (1990) BluRay x264 Ukrainian");
                // "Lang sub" = subtitle language, not audio: French audio must win over the Ukrainian subtitle.
                yield return new TestCaseData("Примарна Індія / L'Inde fantôme (1969) DVDRip Fre / Ukr sub", new List<int> { TorznabCatType.Movies.ID }, true).Returns("L'Inde fantôme (1969) DVDRip French");
                // Pure-Latin original title ("Joy Ride") beats a mixed localized segment ("Check-in у халепу").
                yield return new TestCaseData("Check-in у халепу / Весела поїздочка / Joy Ride (2023) BDRip 1080p 2xUkr/Eng | Sub Eng", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Joy Ride (2023) BluRay 1080p Ukrainian");
                // Heavily homoglyphed source ("WЕВDLRір-АVС") normalized; "Випуски" recognized as episodes.
                yield return new TestCaseData("Вар'яти Шоу (Сезон 1, Випуски 1-10) (2013) WЕВDLRір-АVС", new List<int> { TorznabCatType.TVOther.ID }, true).Returns("Вар'яти Шоу S01E01-E10 (2013) WEBRip x264");

                // --- Format/language standardization round ---
                // BDRemux is a Blu-ray remux -> emit "BluRay Remux" so Sonarr/Radarr score the Remux tier (not plain Bluray).
                yield return new TestCaseData("Дюна / Dune (2021) BDRemux 1080p H.265 Ukr/Eng | Sub Ukr", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Dune (2021) BluRay Remux 1080p x265 Ukrainian");
                // HDR10 and Dolby Vision (DV) markers are preserved for the *arr HDR custom formats.
                yield return new TestCaseData("Аватар / Avatar (2022) BDRemux 2160p HDR10 DV H.265 Ukr/Eng", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Avatar (2022) BluRay Remux 2160p HDR10 DV x265 Ukrainian");
                // Esp -> Spanish, Deu -> German (the ISO-3 codes Toloka uses alongside Spa/Ger).
                yield return new TestCaseData("Лабіринт фавна / El laberinto del fauno (2006) BDRip 1080p Esp | Sub Ukr", new List<int> { TorznabCatType.Movies.ID }, true).Returns("El laberinto del fauno (2006) BluRay 1080p Spanish");
                yield return new TestCaseData("Бункер / Der Untergang (2004) BDRip 1080p Deu | Sub Ukr", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Der Untergang (2004) BluRay 1080p German");
                // Multi-dub prefix written x-first ("x2Ukr", not just "2xUkr") still resolves to Ukrainian audio.
                yield return new TestCaseData("Атака титанів / Attack on Titan (2013) BDRip 1080p H.265 x2Ukr/Jap | Sub Eng", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Attack on Titan (2013) BluRay 1080p x265 Ukrainian");
                // " | " used as a TITLE separator (anime): pick the romaji title AND still read the post-year audio language.
                yield return new TestCaseData("Блакитна коробка | Ao no Hako (2024) WEB-DL 1080p Ukr/Jap", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Ao no Hako (2024) WEB-DL 1080p Ukrainian");

                // --- Zero-floor round: name preservation, episode-count semantics, dates, editions, sources ---
                // Cyrillic-only name is PRESERVED whole (the only Latin is a "[ENG Transfer]" note + "Sub Eng"),
                // never stripped to a lone apostrophe from "Пам'ять".
                yield return new TestCaseData("Крихка Пам'ять (2022) WEB-DL 1080p | Sub Eng [ENG Transfer]", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Крихка Пам'ять (2022) WEB-DL 1080p");
                // Stylized bracket title "[Rec]²": superscript folded, bracket unwrapped -> "Rec 2".
                yield return new TestCaseData("Репортаж 2 / [Rec]² (2009) BDRemux 1080p Ukr/Spa | sub Eng", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Rec 2 (2009) BluRay Remux 1080p Ukrainian");
                // Singular NOUN-first "серія 12 з 12" = the single 12th episode -> E12 (an index, not a 12-ep pack).
                yield return new TestCaseData("Нянпір / Nyanpire The Animation (серія 12 з 12) (2011) HDTVRip Ukr/Jap | Sub Ukr", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Nyanpire The Animation E12 (2011) HDTV Ukrainian");
                // Bare "(12 з 12)" with no keyword = 12 of 12 episodes available -> the full E01-E12 range.
                yield return new TestCaseData("Детективне агентство прекрасних хлопчиків (12 з 12) / Bishounen Tanteidan (2021) BDRip 1080p Ukr/Jap | Ukr Sub", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Bishounen Tanteidan E01-E12 (2021) BluRay 1080p Ukrainian");
                // "серія 1 з 8" = the single 1st episode -> S03E01 (no degenerate S03E01-E01).
                yield return new TestCaseData("Дім дракона (Сезон 3, серія 1 з 8) / House of the Dragon (Season 3) (2026) WEB-DL 1080p 2xUkr/Eng | Sub Eng", new List<int> { TorznabCatType.TV.ID }, true).Returns("House of the Dragon S03E01 (2026) WEB-DL 1080p Ukrainian");
                // "Сезон 1, 5 з 13" = season 1, 5 of 13 episodes available -> S01E01-E05.
                yield return new TestCaseData("Гарлі Квінн (Сезон 1, 5 з 13) / Harley Quinn (Season 1) (2019) BDRip 1080p H.265 Ukr/Eng | Sub Ukr", new List<int> { TorznabCatType.TV.ID }, true).Returns("Harley Quinn S01E01-E05 (2019) BluRay 1080p x265 Ukrainian");
                // Bare (un-parenthesized) year "2019" before the resolution anchors the rebuild; Cyrillic name kept.
                yield return new TestCaseData("Перші ластівки / Сезон 1 (Серії 8 з 8) 2019 480p", new List<int> { TorznabCatType.TV.ID }, true).Returns("Перші ластівки S01E01-E08 (2019) 480p");
                // 2-digit years in a collection list "(1997,2000,02,06)" expand and collapse to a span.
                yield return new TestCaseData("Каю / Кайю (Сезон 1, 7 серій, Сезон 2, 4 серії, Сезон 4, 1 серія, Сезон 5, 2 серії) / Caillou (Season 1, 7 episodes, Season 2, 4episodes, Season 4, 1 episode, Season 5, 1 episodes) (1997,2000,02,06)", new List<int> { TorznabCatType.TV.ID }, true).Returns("Caillou S01-S05 (1997-2006)");
                // A full date "(2014.07.13)" collapses to its year; Cyrillic-only name preserved.
                yield return new TestCaseData("«Вікна-Новини» Спецрепортаж - Слов'янськ після смерті (2014.07.13) SATRip", new List<int> { TorznabCatType.TVDocumentary.ID }, true).Returns("Вікна-Новини Спецрепортаж - Слов'янськ після смерті (2014) HDTV");
                // A source token in the NAME, before the year ("...India Special SatRip (2011)") -> HDTV source slot.
                yield return new TestCaseData("Топ Ґір. Спецвипуск. Індія / Top Gear. India Special SatRip (2011)", new List<int> { TorznabCatType.TVOther.ID }, true).Returns("Top Gear. India Special (2011) HDTV");
                // Reverse homoglyph: a Latin "i" inside Cyrillic "Свiт" is restored so the word survives whole, and a
                // Latin "c" inside "cерiї" lets the episode keyword match ("9 випусків" -> E01-E09).
                yield return new TestCaseData("Свiт Атома (2011) DVB-TVRip-AVC (9 випусків)", new List<int> { TorznabCatType.TVDocumentary.ID }, true).Returns("Світ Атома E01-E09 (2011) TVRip x264");
                yield return new TestCaseData("Кароліна та її друзі (22 cерiї з 52) / Caroline And Her Friends (1994) VHSRip", new List<int> { TorznabCatType.TV.ID }, true).Returns("Caroline And Her Friends E01-E22 (1994) SDTV");
                // Bracketed editions preserved: "[Special Edition]", "[Silent Version]".
                yield return new TestCaseData("Красуня і Чудовисько / Beauty and the Beast (1991) BDRemux 1080p Ukr/Eng | Sub Ukr/Eng [Special Edition]", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Beauty and the Beast (1991) BluRay Remux 1080p Special Edition Ukrainian");
                yield return new TestCaseData("Золота лихоманка / The Gold Rush (1925) BDRip 1080p H.265 Eng | Sub Ukr/Eng [Silent Version]", new List<int> { TorznabCatType.Movies.ID }, true).Returns("The Gold Rush (1925) BluRay 1080p x265 Silent Version English");
                // DVDRemux keeps the lossless "Remux" tier (like BDRemux) instead of collapsing to bare DVD.
                yield return new TestCaseData("Сніговий гонщик / Кевін із півночі / Chilly Dogs / Kevin of the North (2001) DVDRemux 2xUkr/Eng | Sub Eng", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Kevin of the North (2001) DVD Remux Ukrainian");
                // Part info already in the (Cyrillic) name -> the "Part" edition is not appended a second time.
                yield return new TestCaseData("Правила життя. Вся правда про хліб. Частини 1-2 (2010) SatRip", new List<int> { TorznabCatType.TV.ID }, true).Returns("Правила життя. Вся правда про хліб. Частини 1-2 (2010) HDTV");
                yield return new TestCaseData("Відомий Львів невідомий. Частина 1 / Known and Unknown Lviv. Vol. 1 (2005) DVD5 Ukr", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Known and Unknown Lviv. Vol. 1 (2005) DVD Ukrainian");
                // A Latin alias left in parens once the Cyrillic main name is stripped ("Пилосос (Pilesos)") -> "Pilesos".
                yield return new TestCaseData("Пилосос (Pilesos) (2009) | 1-10 серії SiteRip", new List<int> { TorznabCatType.TV.ID }, true).Returns("Pilesos E01-E10 (2009) WEBRip");
                // Alphanumeric short title "F9" and pure-number/numeric titles are valid names (not rejected as junk).
                yield return new TestCaseData("Форсаж 9: Нестримна сага / F9 (2021) BDRip 720p Ukr/Eng | Sub Eng", new List<int> { TorznabCatType.Movies.ID }, true).Returns("F9 (2021) BluRay 720p Ukrainian");
                yield return new TestCaseData("2067: Петля часу / 2067 (2020) BDRip 1080p H.265 2xUkr/Eng | sub Eng", new List<int> { TorznabCatType.Movies.ID }, true).Returns("2067 (2020) BluRay 1080p x265 Ukrainian");
                // Multi-season comma list with a Latin twin -> "H S01-S04".
                yield return new TestCaseData("Лікарня (Сезон 1, 2, 3, 4) / H (Season 1, 2, 3, 4) (1998-2001) DVDRip Ukr/Fre", new List<int> { TorznabCatType.TV.ID }, true).Returns("H S01-S04 (1998-2001) DVDRip Ukrainian");
                // Specials' own episode numbers ("+ specials (episodes 9-10)") must NOT become the main season.
                yield return new TestCaseData("The Blue Planet (season 1) (2001) BDRip 720p + specials (episodes 9-10)", new List<int> { TorznabCatType.TVDocumentary.ID }, true).Returns("The Blue Planet S01 (2001) BluRay 720p Specials");
                // "5 з XXX серії" (unknown total placeholder) -> 5 episodes available -> S01E01-E05.
                yield return new TestCaseData("Пес Патрон (1 сезон 5 з XXX серії) WEBDLRip 720p", new List<int> { TorznabCatType.TV.ID }, true).Returns("Пес Патрон S01E01-E05 WEBRip 720p");

                // --- Wave-1 (multi-agent review) round ---
                // Reverse-homoglyph: "[Усi серiї]" must NOT be forward-homoglyphed to "Yci"; the Cyrillic name is kept.
                yield return new TestCaseData("Ігронавти [Усi серiї] (2011-2016) TVRip-AVC 360p / 480p / 720p", new List<int> { TorznabCatType.TVOther.ID }, true).Returns("Ігронавти (2011-2016) TVRip x264 360p 480p 720p");
                // Music release: "VA" / genre tags are not a title - the Cyrillic album name is preserved.
                yield return new TestCaseData("VA - Український самоспів. Частина перша (2016) DVDRip | Поп, Рок / Караоке", new List<int> { TorznabCatType.TVOther.ID }, true).Returns("VA - Український самоспів. Частина перша (2016) DVDRip");
                // Season + episode range with NO comma; "Серії 3-13 з 26" -> S01E03-E13.
                yield return new TestCaseData("Зелений ліхтар (Сезон 1 Серії 3-13 з 26) / Green Lantern: The Animated Series (Season 1 Episodes 3-13) (2011-2013) WEB-DL 720p Ukr/Eng", new List<int> { TorznabCatType.TV.ID }, true).Returns("Green Lantern: The Animated Series S01E03-E13 (2011-2013) WEB-DL 720p Ukrainian");
                // Number-first season "2 сезон 1-18 серії" = season 2, episodes 1-18 -> S02E01-E18 (not S01-S18).
                yield return new TestCaseData("Коли ми вдома (2 сезон 1-18 серії) (2015) SATRip", new List<int> { TorznabCatType.TV.ID }, true).Returns("Коли ми вдома S02E01-E18 (2015) HDTV");
                // "12 з 116 епізодів" = 12 of 116 -> E01-E12 (uses N, never M).
                yield return new TestCaseData("Скарби зі звалища / Auction Hunters (12 з 116 епізодів) (2010) TVRip", new List<int> { TorznabCatType.TV.ID }, true).Returns("Auction Hunters E01-E12 (2010) TVRip");
                // Number-first season list "(1, 2 сезони)" -> S01-S02.
                yield return new TestCaseData("Привид в латах (1, 2 сезони) / Ghost in the Shell: Stand Alone Complex (2002-2004) BDRip 1080p 2xUkr/Jap | Sub Ukr", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Ghost in the Shell: Stand Alone Complex S01-S02 (2002-2004) BluRay 1080p Ukrainian");
                // "+ 5 Спешлів" is a specials count, not season 5 -> S01 + Specials tag.
                yield return new TestCaseData("Окультна Академія / Seikimatsu Occult Gakuin (Сезон 1 + 5 Спешлів) (2010) BDRip 1080p Ukr/Jap | Sub Ukr", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Seikimatsu Occult Gakuin S01 (2010) BluRay 1080p Specials Ukrainian");
                // Cross-season pack with a parenthetical episode sublist -> the full S01-S03 envelope.
                yield return new TestCaseData("1000 способів померти (Сезон 1, Сезон 2 (2-3, 5-10), Сезон 3) / 1000 Ways To Die (Season 1-3) (2008-2010) HDTVRip", new List<int> { TorznabCatType.TV.ID }, true).Returns("1000 Ways To Die S01-S03 (2008-2010) HDTV");
                // Disc count/range "(Discs 1-16 of 16) 16xDVD9" -> NOT episodes; source is DVD.
                yield return new TestCaseData("Бетмен (Диски 1-16 з 16) / Batman: The Animated Series (Discs 1-16 of 16) 16xDVD9 (1992-1999) Ukr/Eng/Fra | Sub Eng", new List<int> { TorznabCatType.TV.ID }, true).Returns("Batman: The Animated Series (1992-1999) DVD Ukrainian");
                // New source tokens: DVD-5 -> DVD; HDTRip -> HDTV; bare "1080" after a source -> 1080p.
                yield return new TestCaseData("Бандерівці (2008) DVD-5", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Бандерівці (2008) DVD");
                yield return new TestCaseData("Анатомія Грей / Grey's Anatomy Season 6, Episodes 1-4 (2009) HDTRip Eng | sub Ukr", new List<int> { TorznabCatType.TV.ID }, true).Returns("Grey's Anatomy S06E01-E04 (2009) HDTV English");
                yield return new TestCaseData("Кілька / A Few Moments of Cheers (2024) BDRip 1080 Jap", new List<int> { TorznabCatType.Movies.ID }, true).Returns("A Few Moments of Cheers (2024) BluRay 1080p Japanese");
                // Editions: "[Complete Restored Edition]" preserved; "Vol. I-II" + "Частина 1-2" not double-Parted.
                yield return new TestCaseData("Метрополіс / Metropolis [Complete Restored Edition] (1927) BDRip Ger | sub Ukr", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Metropolis (1927) BluRay Complete Restored Edition German");
                yield return new TestCaseData("Німфоманка: Частина 1-2 / Nymphomaniac: Vol. I-II (2013) BDRip-AVC Eng | sub Ukr/Eng", new List<int> { TorznabCatType.Movies.ID }, true).Returns("Nymphomaniac: Vol. I-II (2013) BluRay x264 English");
                // Cyrillic-month year "(Червень 2019)" -> (2019); "/2005 ver" year suffix tolerated.
                yield return new TestCaseData("Збірка музичних відео (Червень 2019) SiteRip 1080р | Pop, Rock, Rap etc.", new List<int> { TorznabCatType.TVOther.ID }, true).Returns("Збірка музичних відео (2019) WEBRip 1080p");
                // Bare year RANGE before a source ("2006-2007 TVRip"); "26 випусків" -> E01-E26.
                yield return new TestCaseData("Феєрія мандрів (26 випусків) 2006-2007 TVRip", new List<int> { TorznabCatType.TV.ID }, true).Returns("Феєрія мандрів E01-E26 (2006-2007) TVRip");
                // "(Mini Series)" -> S01; "+ОВА" -> OVA tag.
                yield return new TestCaseData("Острів скарбів / Treasure Island (Mini Series) (2012) BDRemux 1080p Ukr/Eng | Sub Ukr", new List<int> { TorznabCatType.TV.ID }, true).Returns("Treasure Island S01 (2012) BluRay Remux 1080p Ukrainian");
                yield return new TestCaseData("Поневіряння мага Орфена / Majutsushi Orphen Hagure Tabi (сезон 1+ОВА) (2020) WEBDL 720p", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Majutsushi Orphen Hagure Tabi S01 (2020) WEB-DL 720p OVA");

                // --- Episode-count "of XX / ???" unknown-total placeholder round ---
                // "Сезон 4, серії 11 з ХХ" = season 4, 11 of XX episodes available (a count) -> S04E01-E11, NOT S04E11.
                yield return new TestCaseData("Моє переродження в Слиз (Сезон 4, серії 11 з ХХ) / Tensei shitara Slime Datta Ken (Season 4) (2026) WEBDLRip 1080p H.265 Ukr/Jap | sub Ukr", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Tensei shitara Slime Datta Ken S04E01-E11 (2026) WEBRip 1080p x265 Ukrainian");
                // "Сезон 4, 1-11 з ???" = season 4, episodes 1-11 of unknown total -> S04E01-E11, NOT a season-list S01-S11.
                yield return new TestCaseData("Про моє переродження в слиз (Сезон 4, 1-11 з ???) / Tensei shitara Slime Datta Ken (Season 4) (2026) WEBDLRip 1080p H.264", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Tensei shitara Slime Datta Ken S04E01-E11 (2026) WEBRip 1080p x264");
                // SINGULAR "серія N з ХХ" (unknown total) stays the single Nth episode -> EN (an index, not the count above).
                yield return new TestCaseData("Нянпір / Nyanpire The Animation (серія 5 з ХХ) (2011) HDTVRip Ukr/Jap | Sub Ukr", new List<int> { TorznabCatType.TVAnime.ID }, true).Returns("Nyanpire The Animation E05 (2011) HDTV Ukrainian");
            }
        }
    }
}
