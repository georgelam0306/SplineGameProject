namespace Derp.Doc.Mcp;

internal interface IDerpDocElevenLabsClient
{
    bool TryTextToSpeech(
        string apiBaseUrl,
        string apiKey,
        string voiceId,
        string outputFormat,
        bool? enableLogging,
        string requestJson,
        out byte[] audioBytes,
        out string responseText,
        out string errorMessage);

    bool TrySpeechToSpeech(
        string apiBaseUrl,
        string apiKey,
        string voiceId,
        string outputFormat,
        bool? enableLogging,
        string requestJson,
        byte[] inputAudioBytes,
        string inputAudioFileName,
        string inputAudioMimeType,
        out byte[] audioBytes,
        out string responseText,
        out string errorMessage);
}
