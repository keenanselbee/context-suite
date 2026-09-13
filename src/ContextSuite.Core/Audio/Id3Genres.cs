using System.Globalization;

namespace ContextSuite.Core.Audio;

// Numeric genre assignments from ID3v2.3 Appendix A and its established Winamp
// extensions (126-147). This is format vocabulary, not inferred musical content.
internal static class Id3Genres
{
    private static readonly string[] Names = [
        "Blues", "Classic Rock", "Country", "Dance", "Disco", "Funk", "Grunge", "Hip-Hop", "Jazz", "Metal",
        "New Age", "Oldies", "Other", "Pop", "R&B", "Rap", "Reggae", "Rock", "Techno", "Industrial",
        "Alternative", "Ska", "Death Metal", "Pranks", "Soundtrack", "Euro-Techno", "Ambient", "Trip-Hop", "Vocal", "Jazz+Funk",
        "Fusion", "Trance", "Classical", "Instrumental", "Acid", "House", "Game", "Sound Clip", "Gospel", "Noise",
        "AlternRock", "Bass", "Soul", "Punk", "Space", "Meditative", "Instrumental Pop", "Instrumental Rock", "Ethnic", "Gothic",
        "Darkwave", "Techno-Industrial", "Electronic", "Pop-Folk", "Eurodance", "Dream", "Southern Rock", "Comedy", "Cult", "Gangsta",
        "Top 40", "Christian Rap", "Pop/Funk", "Jungle", "Native American", "Cabaret", "New Wave", "Psychadelic", "Rave", "Showtunes",
        "Trailer", "Lo-Fi", "Tribal", "Acid Punk", "Acid Jazz", "Polka", "Retro", "Musical", "Rock & Roll", "Hard Rock",
        "Folk", "Folk-Rock", "National Folk", "Swing", "Fast Fusion", "Bebob", "Latin", "Revival", "Celtic", "Bluegrass",
        "Avantgarde", "Gothic Rock", "Progressive Rock", "Psychedelic Rock", "Symphonic Rock", "Slow Rock", "Big Band", "Chorus", "Easy Listening", "Acoustic",
        "Humour", "Speech", "Chanson", "Opera", "Chamber Music", "Sonata", "Symphony", "Booty Bass", "Primus", "Porn Groove",
        "Satire", "Slow Jam", "Club", "Tango", "Samba", "Folklore", "Ballad", "Power Ballad", "Rhythmic Soul", "Freestyle",
        "Duet", "Punk Rock", "Drum Solo", "A cappella", "Euro-House", "Dance Hall", "Goa", "Drum & Bass", "Club-House", "Hardcore",
        "Terror", "Indie", "BritPop", "Negerpunk", "Polsk Punk", "Beat", "Christian Gangsta", "Heavy Metal", "Black Metal", "Crossover",
        "Contemporary Christian", "Christian Rock", "Merengue", "Salsa", "Thrash Metal", "Anime", "JPop", "SynthPop"
    ];

    internal static string FromNumber(int value)
    {
        if ((uint)value >= Names.Length) throw new NotSupportedException("This numeric ID3 genre needs a reviewed mapping.");
        return Names[value];
    }

    internal static string FromText(string value, int version)
    {
        if (value.Length == 0) return value;
        if (version <= 3 && value.StartsWith("((", StringComparison.Ordinal)) return value[1..];
        var code = value; var refinement = "";
        if (value.StartsWith('('))
        {
            var end = value.IndexOf(')');
            if (end < 2) throw new NotSupportedException("ID3 genre references need a valid mapping.");
            code = value[1..end]; refinement = value[(end + 1)..];
            if (version == 4 && !code.All(char.IsAsciiDigit) && code is not ("RX" or "CR")) return value;
        }
        string name;
        if (code == "RX") name = "Remix";
        else if (code == "CR") name = "Cover";
        else if (code.All(char.IsAsciiDigit) && int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            name = FromNumber(number);
        else if (!value.StartsWith('(') && !code.All(char.IsAsciiDigit)) return value;
        else throw new NotSupportedException("ID3 genre references need a reviewed mapping.");
        if (refinement.Length != 0 && !refinement.Equals(name, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Multiple or refined ID3 genres need a mapping that retains every value.");
        return name;
    }
}
