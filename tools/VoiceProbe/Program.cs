using System.Text;
using Windows.Globalization;
using Windows.Media.SpeechRecognition;

// The 0.7.0 voice spike: one question, answered on a real PC. Does Windows.Media.SpeechRecognition.SpeechRecognizer
// with a list constraint (offline, no dictation service) recognise the bar's names in zh-TW from an unpackaged
// process on Windows 11? Arguments: a language tag, then the phrases to listen for.
Console.OutputEncoding = Encoding.UTF8;
var tag = args.Length > 0 ? args[0] : "zh-TW";
string[] phrases = args.Length > 1 ? args[1..] : ["記事本", "小算盤", "檔案總管", "命令提示字元", "小畫家"];

Console.WriteLine($"Windows {Environment.OSVersion.Version}, {(Environment.Is64BitProcess ? "64" : "32")}-bit process, not packaged");
Console.WriteLine($"Speech languages installed: {string.Join(", ", SpeechRecognizer.SupportedGrammarLanguages.Select(l => l.LanguageTag))}");
Console.WriteLine($"Windows speech language: {SpeechRecognizer.SystemSpeechLanguage?.LanguageTag ?? "(none)"}");

try
{
    using var recognizer = new SpeechRecognizer(new Language(tag));
    recognizer.Constraints.Add(new SpeechRecognitionListConstraint(phrases, "names"));
    var compiled = await recognizer.CompileConstraintsAsync();
    Console.WriteLine($"Recogniser for {recognizer.CurrentLanguage.LanguageTag}: compile {compiled.Status}");
    if (compiled.Status != SpeechRecognitionResultStatus.Success)
    {
        Console.WriteLine("ANSWER: no (the list could not be compiled; is the speech pack for this language installed?)");
        return 2;
    }

    var heard = 0;
    for (var attempt = 1; attempt <= 3; attempt++)
    {
        Console.WriteLine($"Say one of: {string.Join(" / ", phrases)}   (try {attempt} of 3)");
        var result = await recognizer.RecognizeAsync();
        Console.WriteLine($"  {result.Status}: \"{result.Text}\", confidence {result.Confidence} ({result.RawConfidence:0.00})");
        if (result.Status == SpeechRecognitionResultStatus.Success && result.Text.Length > 0)
        {
            heard++;
        }
    }

    Console.WriteLine(heard > 0
        ? $"ANSWER: yes ({heard} of 3 heard; check above that they were the names you said)"
        : "ANSWER: no (nothing recognised; check the microphone and Settings > Privacy & security > Microphone > Let desktop apps access your microphone)");
    return heard > 0 ? 0 : 3;
}
catch (Exception ex)
{
    // Failures to tell apart: 0x80070005 (access denied: the microphone switch for desktop apps is off), 0x80045509
    // (the online speech privacy consent, which an offline list constraint should not need), or an exception for the
    // language itself (no speech pack for it).
    Console.WriteLine($"Failed: 0x{ex.HResult:X8} {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine("ANSWER: no (see the error above)");
    return 1;
}
