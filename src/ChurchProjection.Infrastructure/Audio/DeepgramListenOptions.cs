using Deepgram.Models.Listen.v2.WebSocket;

namespace ChurchProjection.Infrastructure.Audio;

/// <summary>
/// Streaming listen options for live operator captions. Built in one place so latency knobs
/// (endpointing, unused utterance-end events) can be tested without opening a WebSocket.
/// </summary>
public static class DeepgramListenOptions
{
    public static LiveSchema Create(int sampleRate) => new()
    {
        Model = "nova-3",
        Encoding = "linear16",
        SampleRate = sampleRate,
        Channels = 1,
        Language = "en",
        Punctuate = true,
        SmartFormat = true,
        Numerals = true,
        InterimResults = true,
        // 100ms of silence before a final (was 400). Interims still stream; this only
        // makes locked-in captions catch up sooner during continuous speech.
        EndPointing = "100",
        FillerWords = false,
        NoDelay = true,
        Keyterm = [
            "Genesis", "Exodus", "Leviticus", "Numbers", "Deuteronomy",
            "Joshua", "Judges", "Ruth", "Samuel", "Kings", "Chronicles",
            "Ezra", "Nehemiah", "Esther", "Job", "Psalms", "Psalm",
            "Proverbs", "Ecclesiastes", "Song of Solomon", "Isaiah",
            "Jeremiah", "Lamentations", "Ezekiel", "Daniel", "Hosea",
            "Joel", "Amos", "Obadiah", "Jonah", "Micah", "Nahum",
            "Habakkuk", "Zephaniah", "Haggai", "Zechariah", "Malachi",
            "Matthew", "Mark", "Luke", "John", "Acts", "Romans",
            "Corinthians", "Galatians", "Ephesians", "Philippians",
            "Colossians", "Thessalonians", "Timothy", "Titus", "Philemon",
            "Hebrews", "James", "Peter", "Jude", "Revelation",
            "First", "Second", "Third", "chapter", "verse", "verses",
            "scripture", "passage", "gospel", "epistle", "parable",
            "amen", "hallelujah", "Jesus", "Christ", "God", "Holy Spirit",
            "Lord", "covenant", "righteousness", "salvation", "grace",
            "disciples", "apostle", "prophet", "Messiah", "Yahweh",
        ],
    };
}
