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

// What the second run (en-US, "Internal Speech Error") could not tell: is there a microphone at all, which step
// failed, and which of the older desktop recognisers (SAPI, the fallback) this PC has.
try
{
    var microphones = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(Windows.Devices.Enumeration.DeviceClass.AudioCapture);
    Console.WriteLine($"Microphones: {(microphones.Count == 0 ? "(none)" : string.Join(", ", microphones.Select(m => $"{m.Name}{(m.IsEnabled ? string.Empty : " [disabled]")}{(m.IsDefault ? " [default]" : string.Empty)}")))}");
}
catch (Exception ex)
{
    Console.WriteLine($"Microphones: could not be listed (0x{ex.HResult:X8} {ex.Message.Trim()})");
}

try
{
    if (Type.GetTypeFromProgID("SAPI.SpInprocRecognizer") is { } sapi && Activator.CreateInstance(sapi) is { } created)
    {
        dynamic desktop = created;
        dynamic tokens = desktop.GetRecognizers(string.Empty, string.Empty);
        var names = new List<string>();
        for (var i = 0; i < (int)tokens.Count; i++)
        {
            names.Add((string)tokens.Item(i).GetDescription(0));
        }

        Console.WriteLine($"Desktop (SAPI) recognisers: {(names.Count == 0 ? "(none)" : string.Join("; ", names))}");
    }
    else
    {
        Console.WriteLine("Desktop (SAPI) recognisers: SAPI is not there");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Desktop (SAPI) recognisers: could not be listed (0x{ex.HResult:X8} {ex.Message.Trim()})");
}

var step = "creating the recogniser";
try
{
    using var recognizer = new SpeechRecognizer(new Language(tag));
    step = "adding the list of names";
    recognizer.Constraints.Add(new SpeechRecognitionListConstraint(phrases, "names"));
    step = "compiling the list";
    var compiled = await recognizer.CompileConstraintsAsync();
    step = "listening";
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
    Console.WriteLine($"Failed while {step}: 0x{ex.HResult:X8} {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine("ANSWER (Windows.Media.SpeechRecognition): no (see the error above)");
}

// The fallback: the desktop recognisers (SAPI), which the fourth run found for zh-TW and en-US on a PC where the
// recogniser above could not be created for either. The same question, asked of them.
Console.WriteLine();
Console.WriteLine("Trying the desktop (SAPI) recogniser for the same names…");
try
{
    var culture = new System.Globalization.CultureInfo(tag);
    using var engine = new System.Speech.Recognition.SpeechRecognitionEngine(culture);
    Console.WriteLine($"Recogniser: {engine.RecognizerInfo.Description}");
    var builder = new System.Speech.Recognition.GrammarBuilder(new System.Speech.Recognition.Choices(phrases)) { Culture = culture };
    engine.LoadGrammar(new System.Speech.Recognition.Grammar(builder));
    engine.SetInputToDefaultAudioDevice();
    var heard = 0;
    for (var attempt = 1; attempt <= 3; attempt++)
    {
        Console.WriteLine($"Say one of: {string.Join(" / ", phrases)}   (try {attempt} of 3, 8 seconds)");
        var result = engine.Recognize(TimeSpan.FromSeconds(8));
        Console.WriteLine(result is null ? "  nothing recognised" : $"  \"{result.Text}\", confidence {result.Confidence:0.00}");
        if (result is { Text.Length: > 0 })
        {
            heard++;
        }
    }

    Console.WriteLine(heard > 0
        ? $"ANSWER (SAPI): yes ({heard} of 3 heard; check above that they were the names you said)"
        : "ANSWER (SAPI): no (nothing recognised; is the default recording device the microphone you spoke into?)");
    return heard > 0 ? 0 : 3;
}
catch (Exception ex)
{
    Console.WriteLine($"Failed: 0x{ex.HResult:X8} {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine("ANSWER (SAPI): no (see the error above)");
    return 1;
}
