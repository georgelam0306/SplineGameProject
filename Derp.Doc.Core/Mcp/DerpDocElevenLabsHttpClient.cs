using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Derp.Doc.Mcp;

internal sealed class DerpDocElevenLabsHttpClient : IDerpDocElevenLabsClient
{
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    public bool TryTextToSpeech(
        string apiBaseUrl,
        string apiKey,
        string voiceId,
        string outputFormat,
        bool? enableLogging,
        string requestJson,
        out byte[] audioBytes,
        out string responseText,
        out string errorMessage)
    {
        return TryInvoke(
            apiBaseUrl,
            "/v1/text-to-speech/" + Uri.EscapeDataString(voiceId),
            apiKey,
            outputFormat,
            enableLogging,
            requestJson,
            inputAudioBytes: null,
            inputAudioFileName: null,
            inputAudioMimeType: null,
            out audioBytes,
            out responseText,
            out errorMessage);
    }

    public bool TrySpeechToSpeech(
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
        out string errorMessage)
    {
        return TryInvoke(
            apiBaseUrl,
            "/v1/speech-to-speech/" + Uri.EscapeDataString(voiceId),
            apiKey,
            outputFormat,
            enableLogging,
            requestJson,
            inputAudioBytes,
            inputAudioFileName,
            inputAudioMimeType,
            out audioBytes,
            out responseText,
            out errorMessage);
    }

    private static bool TryInvoke(
        string apiBaseUrl,
        string endpointPath,
        string apiKey,
        string outputFormat,
        bool? enableLogging,
        string requestJson,
        byte[]? inputAudioBytes,
        string? inputAudioFileName,
        string? inputAudioMimeType,
        out byte[] audioBytes,
        out string responseText,
        out string errorMessage)
    {
        audioBytes = Array.Empty<byte>();
        responseText = "";
        errorMessage = "";

        if (!TryBuildEndpointUri(apiBaseUrl, endpointPath, outputFormat, enableLogging, out string endpointUri, out errorMessage))
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri);
            request.Headers.TryAddWithoutValidation("xi-api-key", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/wav"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (inputAudioBytes == null)
            {
                request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            }
            else
            {
                request.Content = BuildSpeechToSpeechContent(
                    requestJson,
                    inputAudioBytes,
                    inputAudioFileName ?? "input_audio.wav",
                    string.IsNullOrWhiteSpace(inputAudioMimeType) ? "audio/wav" : inputAudioMimeType);
            }

            using HttpResponseMessage response = SharedHttpClient.Send(request);
            byte[] payload = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                string bodySnippet = payload.Length > 0
                    ? Truncate(Encoding.UTF8.GetString(payload), 400)
                    : "";
                errorMessage = string.IsNullOrWhiteSpace(bodySnippet)
                    ? "ElevenLabs request failed with HTTP " + (int)response.StatusCode + "."
                    : "ElevenLabs request failed with HTTP " + (int)response.StatusCode + ": " + bodySnippet;
                return false;
            }

            if (TryExtractAudioBytesFromPayload(response, payload, out audioBytes, out responseText, out errorMessage))
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            errorMessage = "ElevenLabs request failed: " + ex.Message;
            return false;
        }
    }

    private static MultipartFormDataContent BuildSpeechToSpeechContent(
        string requestJson,
        byte[] inputAudioBytes,
        string inputAudioFileName,
        string inputAudioMimeType)
    {
        var multipartContent = new MultipartFormDataContent();

        var audioContent = new ByteArrayContent(inputAudioBytes);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue(inputAudioMimeType);
        multipartContent.Add(audioContent, "audio", inputAudioFileName);

        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return multipartContent;
        }

        using JsonDocument requestDocument = JsonDocument.Parse(requestJson);
        if (requestDocument.RootElement.ValueKind != JsonValueKind.Object)
        {
            return multipartContent;
        }

        foreach (JsonProperty property in requestDocument.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            string fieldValue = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? "",
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => property.Value.GetRawText(),
            };

            multipartContent.Add(new StringContent(fieldValue, Encoding.UTF8), property.Name);
        }

        return multipartContent;
    }

    private static bool TryBuildEndpointUri(
        string apiBaseUrl,
        string endpointPath,
        string outputFormat,
        bool? enableLogging,
        out string endpointUri,
        out string errorMessage)
    {
        endpointUri = "";
        errorMessage = "";

        string normalizedBaseUrl = (apiBaseUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalizedBaseUrl))
        {
            errorMessage = "Missing ElevenLabs base URL.";
            return false;
        }

        if (!normalizedBaseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            normalizedBaseUrl += "/";
        }

        if (!Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out Uri? baseUri))
        {
            errorMessage = "ElevenLabs base URL is invalid: " + normalizedBaseUrl;
            return false;
        }

        string normalizedEndpointPath = string.IsNullOrWhiteSpace(endpointPath)
            ? ""
            : endpointPath.TrimStart('/');
        Uri resolvedUri = string.IsNullOrWhiteSpace(normalizedEndpointPath)
            ? baseUri
            : new Uri(baseUri, normalizedEndpointPath);

        var queryPairs = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(outputFormat))
        {
            queryPairs.Add("output_format=" + Uri.EscapeDataString(outputFormat));
        }

        if (enableLogging.HasValue)
        {
            queryPairs.Add("enable_logging=" + (enableLogging.Value ? "true" : "false"));
        }

        if (queryPairs.Count <= 0)
        {
            endpointUri = resolvedUri.ToString();
            return true;
        }

        var builder = new UriBuilder(resolvedUri)
        {
            Query = string.Join("&", queryPairs),
        };
        endpointUri = builder.Uri.ToString();
        return true;
    }

    private static bool TryExtractAudioBytesFromPayload(
        HttpResponseMessage response,
        byte[] payload,
        out byte[] audioBytes,
        out string responseText,
        out string errorMessage)
    {
        audioBytes = Array.Empty<byte>();
        responseText = "";
        errorMessage = "";

        string mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            audioBytes = payload;
            responseText = "";
            return true;
        }

        string responseJson = Encoding.UTF8.GetString(payload);
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            errorMessage = "ElevenLabs response body is empty.";
            return false;
        }

        responseText = responseJson;
        if (!TryExtractAudioBytesFromResponseJson(responseJson, out audioBytes, out errorMessage))
        {
            return false;
        }

        return true;
    }

    private static bool TryExtractAudioBytesFromResponseJson(
        string responseJson,
        out byte[] audioBytes,
        out string errorMessage)
    {
        audioBytes = Array.Empty<byte>();
        errorMessage = "";

        try
        {
            using var document = JsonDocument.Parse(responseJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "ElevenLabs response must be a JSON object.";
                return false;
            }

            if (TryExtractAudioFromStringProperty(root, "audio_base64", out audioBytes, out errorMessage))
            {
                return true;
            }

            if (TryExtractAudioFromStringProperty(root, "audioBase64", out audioBytes, out errorMessage))
            {
                return true;
            }

            if (TryExtractAudioFromStringProperty(root, "audio", out audioBytes, out errorMessage))
            {
                return true;
            }

            if (TryExtractAudioFromArrayObjectProperty(root, "data", out audioBytes, out errorMessage))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            errorMessage = "Failed parsing ElevenLabs JSON response: " + ex.Message;
            return false;
        }

        errorMessage = "ElevenLabs response did not include audio bytes.";
        return false;
    }

    private static bool TryExtractAudioFromArrayObjectProperty(
        JsonElement root,
        string propertyName,
        out byte[] audioBytes,
        out string errorMessage)
    {
        audioBytes = Array.Empty<byte>();
        errorMessage = "";

        if (!root.TryGetProperty(propertyName, out JsonElement arrayElement) ||
            arrayElement.ValueKind != JsonValueKind.Array ||
            arrayElement.GetArrayLength() <= 0)
        {
            return false;
        }

        JsonElement firstItem = arrayElement[0];
        if (firstItem.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryExtractAudioFromStringProperty(firstItem, "audio_base64", out audioBytes, out errorMessage))
        {
            return true;
        }

        if (TryExtractAudioFromStringProperty(firstItem, "audioBase64", out audioBytes, out errorMessage))
        {
            return true;
        }

        if (TryExtractAudioFromStringProperty(firstItem, "audio", out audioBytes, out errorMessage))
        {
            return true;
        }

        return false;
    }

    private static bool TryExtractAudioFromStringProperty(
        JsonElement root,
        string propertyName,
        out byte[] audioBytes,
        out string errorMessage)
    {
        audioBytes = Array.Empty<byte>();
        errorMessage = "";

        if (!root.TryGetProperty(propertyName, out JsonElement propertyElement) ||
            propertyElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string encodedPayload = propertyElement.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(encodedPayload))
        {
            errorMessage = "ElevenLabs response field '" + propertyName + "' is empty.";
            return false;
        }

        string normalizedPayload = encodedPayload.Trim();
        if (normalizedPayload.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int commaIndex = normalizedPayload.IndexOf(',');
            if (commaIndex < 0 || commaIndex >= normalizedPayload.Length - 1)
            {
                errorMessage = "ElevenLabs audio data URL is invalid.";
                return false;
            }

            normalizedPayload = normalizedPayload[(commaIndex + 1)..];
        }

        try
        {
            audioBytes = Convert.FromBase64String(normalizedPayload);
            return true;
        }
        catch (FormatException)
        {
            errorMessage = "ElevenLabs response field '" + propertyName + "' is not valid base64.";
            return false;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(120);
        return client;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return text.Length <= maxLength
            ? text
            : text[..maxLength] + "...";
    }
}
