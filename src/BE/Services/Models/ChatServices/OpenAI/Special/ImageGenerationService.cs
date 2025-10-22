﻿using OpenAI;
using System.ClientModel;
using Chats.BE.DB;
using OpenAI.Images;
using Chats.BE.Services.Models.Dtos;
using OpenAI.Chat;
using Chats.BE.DB.Enums;
using System.Text.Json.Nodes;
using System.ClientModel.Primitives;
using Chats.BE.Services.FileServices;
using System.Runtime.CompilerServices;
using GeneratedImageSize = OpenAI.Images.GeneratedImageSize;
using System.Net.ServerSentEvents;
using System.Diagnostics;
using System.Text.Json;

namespace Chats.BE.Services.Models.ChatServices.OpenAI.Special;

public class ImageGenerationService(Model model, ImageClient imageClient) : ChatService(model)
{
    private DBReasoningEffort _reasoningEffort;
    private DBKnownImageSize _imageSize;

    public ImageGenerationService(Model model, Uri? suggestedUri = null, params PipelinePolicy[] perCallPolicies) : this(model, CreateImageGenerationAPI(model, suggestedUri, perCallPolicies))
    {
    }

    private static ImageClient CreateImageGenerationAPI(Model model, Uri? suggestedUrl, PipelinePolicy[] perCallPolicies)
    {
        OpenAIClient api = ChatCompletionService.CreateOpenAIClient(model, suggestedUrl, perCallPolicies);
        ImageClient cc = api.GetImageClient(model.ApiModelId);
        return cc;
    }

    public override async IAsyncEnumerable<ChatSegment> ChatStreamed(IReadOnlyList<ChatMessage> messages, ChatCompletionOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string prompt = GetPromptStatic(messages);
        ChatMessageContentPart[] images = GetImagesStatic(messages);

        ClientPipeline pipeline = imageClient.Pipeline;

        if (images.Length == 0)
        {
            // Generate images API with streaming
            Console.WriteLine("=== Generating images with streaming ===");

            JsonObject requestBody = new()
            {
                ["prompt"] = prompt,
                ["model"] = Model.ApiModelId,
                ["n"] = options.MaxOutputTokenCount ?? 1,
                ["stream"] = true,
                ["partial_images"] = 3,
                ["moderation"] = "low"
            };

            if (_reasoningEffort != DBReasoningEffort.Default)
            {
                requestBody["quality"] = _reasoningEffort.ToGeneratedImageQualityText();
            }

            if (_imageSize != DBKnownImageSize.Default)
            {
                requestBody["size"] = _imageSize switch
                {
                    DBKnownImageSize.W1024xH1024 => "1024x1024",
                    DBKnownImageSize.W1536xH1024 => "1536x1024",
                    DBKnownImageSize.W1024xH1536 => "1024x1536",
                    _ => throw new NotSupportedException($"Unsupported image size: {_imageSize}"),
                };
            }
            else
            {
                if (prompt.Contains("3:2"))
                {
                    requestBody["size"] = "1536x1024";
                }
                else if (prompt.Contains("2:3"))
                {
                    requestBody["size"] = "1024x1536";
                }
            }

            if (options.EndUserId != null)
            {
                requestBody["user"] = options.EndUserId;
            }

            string jsonContent = requestBody.ToJsonString();
            Console.WriteLine($"Request body: {jsonContent}");

            BinaryContent content = BinaryContent.Create(BinaryData.FromString(jsonContent));
            PipelineMessage message = pipeline.CreateMessage();
            message.Request.Method = "POST";
            message.Request.Uri = new Uri(imageClient.Endpoint, "v1/images/generations");
            message.Request.Headers.Set("Content-Type", "application/json");
            message.Request.Headers.Set("Accept", "text/event-stream");
            message.Request.Content = content;
            message.BufferResponse = false;

            Stopwatch sw = Stopwatch.StartNew();
            await pipeline.SendAsync(message).ConfigureAwait(false);

            // Check response status
            if (message.Response == null)
            {
                throw new Exception("No response received from the server.");
            }

            if (!message.Response.IsError)
            {
                await foreach (ChatSegment segment in ProcessImageStreamResponseAsync(message.Response, sw, "image_gen_sse", cancellationToken))
                {
                    yield return segment;
                }
            }
            else
            {
                ThrowErrorResponse(message.Response, cancellationToken);
            }
        }
        else
        {
            // Image edits API with streaming
            Console.WriteLine("=== Generating image edits with streaming ===");

            MultiPartFormDataBinaryContent form = await BuildImageEditFormAsync(images, prompt, options, cancellationToken);
            form.Add("true", "stream");
            form.Add("3", "partial_images");

            Console.WriteLine("Request form fields:");
            Console.WriteLine($"  prompt: {prompt}");
            Console.WriteLine($"  model: {Model.ApiModelId}");
            Console.WriteLine($"  n: {options.MaxOutputTokenCount ?? 1}");
            Console.WriteLine($"  stream: true");
            Console.WriteLine($"  partial_images: 3");

            PipelineMessage message = pipeline.CreateMessage();
            message.Request.Method = "POST";
            message.Request.Uri = new Uri(imageClient.Endpoint, "v1/images/edits");
            message.Request.Headers.Set("Content-Type", form.ContentType);
            message.Request.Headers.Set("Accept", "text/event-stream");
            message.Request.Content = form;
            message.BufferResponse = false;

            Stopwatch sw = Stopwatch.StartNew();
            await pipeline.SendAsync(message).ConfigureAwait(false);

            // Check response status
            if (message.Response == null)
            {
                throw new Exception("No response received from the server.");
            }

            if (!message.Response.IsError)
            {
                await foreach (ChatSegment segment in ProcessImageStreamResponseAsync(message.Response, sw, "image_edit_sse", cancellationToken))
                {
                    yield return segment;
                }
            }
            else
            {
                ThrowErrorResponse(message.Response, cancellationToken);
            }
        }

        yield break;
    }

    public override async Task<ChatSegment> Chat(IReadOnlyList<ChatMessage> messages, ChatCompletionOptions options, CancellationToken cancellationToken)
    {
        string prompt = GetPromptStatic(messages);
        ChatMessageContentPart[] images = GetImagesStatic(messages);
        ClientResult<GeneratedImageCollection> cr = null!;
        if (images.Length == 0)
        {
            cr = await imageClient.GenerateImagesAsync(
                prompt,
                options.MaxOutputTokenCount ?? 1,
                new ImageGenerationOptions()
                {
                    EndUserId = options.EndUserId,
                    Quality = _reasoningEffort.ToGeneratedImageQuality(),
                    Size = _imageSize switch
                    {
                        DBKnownImageSize.Default => prompt.Contains("3:2") ? GeneratedImageSize.W1536xH1024 : prompt.Contains("2:3") ? GeneratedImageSize.W1024xH1536 : null,
                        DBKnownImageSize.W1024xH1024 => GeneratedImageSize.W1024xH1024,
                        DBKnownImageSize.W1536xH1024 => GeneratedImageSize.W1536xH1024,
                        DBKnownImageSize.W1024xH1536 => GeneratedImageSize.W1024xH1536,
                        _ => throw new NotSupportedException($"Unsupported image size: {_imageSize}"),
                    },
                    ModerationLevel = GeneratedImageModerationLevel.Low,
                }, cancellationToken);
        }
        else
        {
            MultiPartFormDataBinaryContent form = await BuildImageEditFormAsync(images, prompt, options, cancellationToken);

            ClientResult clientResult = await imageClient.GenerateImageEditsAsync(form, form.ContentType, new RequestOptions()
            {
                CancellationToken = cancellationToken
            });
            cr = ClientResult.FromValue((GeneratedImageCollection)clientResult, clientResult.GetRawResponse());
        }

        JsonObject rawJson = cr.GetRawResponse().Content
                .ToObjectFromJson<JsonObject>() ?? throw new Exception("Unable to parse raw JSON from the response.");
        GeneratedImageCollection gic = cr.Value ?? throw new Exception("Unable to parse generated image collection from the response.");
        JsonNode usage = rawJson["usage"] ?? throw new Exception("Unable to parse usage from the response.");

        return new ChatSegment()
        {
            FinishReason = null,
            Items = [.. cr.Value.Select(x => ChatSegmentItem.FromBinaryData(x.ImageBytes, "image/png"))],
            Usage = new Dtos.ChatTokenUsage()
            {
                InputTokens = usage["input_tokens"]!.GetValue<int>(),
                OutputTokens = usage["output_tokens"]!.GetValue<int>(),
                ReasoningTokens = 0,
            },
        };
    }

    private static string GetPromptStatic(IReadOnlyList<ChatMessage> messages)
    {
        UserChatMessage? userMessage = messages.OfType<UserChatMessage>().LastOrDefault();
        if (userMessage != null)
        {
            ChatMessageContentPart? textPart = userMessage.Content
                .Where(x => x.Kind == ChatMessageContentPartKind.Text)
                .LastOrDefault();
            if (textPart != null)
            {
                return textPart.Text;
            }
        }
        throw new InvalidOperationException($"Unable to find a text part in the user message.");
    }

    private static ChatMessageContentPart[] GetImagesStatic(IReadOnlyList<ChatMessage> messages)
    {
        // if user message contains image, we need to use all images in the message as input
        UserChatMessage? userChatMessage = messages.OfType<UserChatMessage>().LastOrDefault();
        if (userChatMessage != null)
        {
            ChatMessageContentPart[] userMessageImages = [.. userChatMessage.Content.Where(x => x.Kind == ChatMessageContentPartKind.Image)];
            if (userMessageImages.Length > 0)
            {
                return userMessageImages;
            }
        }

        // otherwise, we need to use the last image in the message as input
        return [.. messages.SelectMany(x => x.Content)
            .Where(x => x.Kind == ChatMessageContentPartKind.Image)
            .Reverse()
            .Take(1)];
    }

    private async Task<MultiPartFormDataBinaryContent> BuildImageEditFormAsync(
        ChatMessageContentPart[] images,
        string prompt,
        ChatCompletionOptions options,
        CancellationToken cancellationToken)
    {
        using HttpClient http = new();
        MultiPartFormDataBinaryContent form = new();

        Dictionary<Uri, HttpResponseMessage> downloadedFiles = (await Task.WhenAll(images
            .Where(x => x.ImageUri != null)
            .GroupBy(x => x.ImageUri).Select(async image =>
            {
                HttpResponseMessage resp = await http.GetAsync(image.Key, cancellationToken);
                return (url: image.Key, resp);
            })))
        .ToDictionary(k => k.url, v => v.resp);

        foreach (ChatMessageContentPart image in images)
        {
            if (image.ImageUri != null)
            {
                HttpResponseMessage file = downloadedFiles[image.ImageUri];
                string fileName = Path.GetFileName(image.ImageUri.LocalPath);
                if (fileName.Contains("mask.png"))
                {
                    form.Add(await file.Content.ReadAsStreamAsync(cancellationToken), "mask", fileName, file.Content.Headers.ContentType?.ToString());
                }
                else
                {
                    form.Add(await file.Content.ReadAsStreamAsync(cancellationToken), "image", fileName, file.Content.Headers.ContentType?.ToString());
                }
            }
            else
            {
                form.Add(image.ImageBytes, "image", image.Filename ?? DBFileDef.MakeFileNameByContentType(image.ImageBytesMediaType), image.ImageBytesMediaType);
            }
        }

        form.Add(prompt, "prompt");
        form.Add(options.MaxOutputTokenCount ?? 1, "n");
        form.Add(Model.ApiModelId, "model");

        if (_imageSize != DBKnownImageSize.Default)
        {
            form.Add(_imageSize switch
            {
                DBKnownImageSize.W1024xH1024 => "1024x1024",
                DBKnownImageSize.W1536xH1024 => "1536x1024",
                DBKnownImageSize.W1024xH1536 => "1024x1536",
                _ => throw new NotSupportedException($"Unsupported image size: {_imageSize}"),
            }, "size");
        }
        else
        {
            if (prompt.Contains("3:2"))
            {
                form.Add("1536x1024", "size");
            }
            else if (prompt.Contains("2:3"))
            {
                form.Add("1024x1536", "size");
            }
        }

        if (options.EndUserId != null)
        {
            form.Add(options.EndUserId, "user");
        }

        form.Add("low", "moderation");

        if (_reasoningEffort != DBReasoningEffort.Default)
        {
            form.Add(_reasoningEffort.ToGeneratedImageQualityText()!, "quality");
        }

        return form;
    }

    private static string GetContentTypeFromOutputFormat(string outputFormat) => outputFormat.ToLowerInvariant() switch
    {
        "png" => "image/png",
        "jpeg" => "image/jpeg",
        "jpg" => "image/jpeg",
        "webp" => "image/webp",
        _ => "image/png", // default to png
    };

    private static async IAsyncEnumerable<ChatSegment> ProcessImageStreamResponseAsync(
        PipelineResponse response,
        Stopwatch sw,
        string logPrefix,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (response.ContentStream == null)
        {
            yield break;
        }

        string baseTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string logDir = Path.Combine(Path.GetTempPath(), $"{logPrefix}_{baseTimestamp}");
        Directory.CreateDirectory(logDir);
        Console.WriteLine($"SSE responses will be saved to: {logDir}");

        int eventIndex = 0;

        await foreach (SseItem<string> item in SseParser.Create(response.ContentStream).EnumerateAsync(cancellationToken))
        {
            string logFileName = Path.Combine(logDir, $"{eventIndex:D3}_{item.EventType}_{sw.Elapsed.TotalSeconds:F3}s.json");
            await System.IO.File.WriteAllTextAsync(logFileName, item.Data, cancellationToken);
            Console.WriteLine($"[{sw.Elapsed.TotalSeconds:F3}s] SSE Event: {item.EventType} -> {Path.GetFileName(logFileName)}");

            JsonDocument doc = JsonDocument.Parse(item.Data);
            JsonElement root = doc.RootElement;
            string eventType = root.GetProperty("type").GetString()!;

            // Partial image event
            // Format: 
            // {"type":"image_generation.partial_image","partial_image_index":0,"b64_json":"...","created_at":1761112880,"size":"1024x1024","quality":"high","background":"opaque","output_format":"png"}
            // Format: 
            // {
            //   "type": "image_edit.partial_image",
            //   "partial_image_index": 0,
            //   "b64_json": "...",
            //   "created_at": 1761116613,
            //   "size": "1024x1024",
            //   "quality": "high",
            //   "background": "opaque",
            //   "output_format": "png"
            // }
            if (eventType == "image_generation.partial_image" || eventType == "image_edit.partial_image")
            {
                string b64Json = root.GetProperty("b64_json").GetString()!;
                string outputFormat = root.GetProperty("output_format").GetString()!;
                string contentType = GetContentTypeFromOutputFormat(outputFormat);

                // Yield partial image
                yield return new ChatSegment()
                {
                    FinishReason = null,
                    Items = [ChatSegmentItem.FromBase64Image(b64Json, contentType)],
                    Usage = null,
                };
            }
            // Completed image event
            // image_generation.completed:
            // {
            //   "type":"image_generation.completed",
            //   "b64_json":"...",
            //   "created_at":1761105722,
            //   "usage": {
            //     "input_tokens": 7,
            //     "output_tokens": 4460,
            //     "total_tokens": 4467,
            //     "input_tokens_details":{
            //       "text_tokens": 7,
            //       "image_tokens": 0
            //     }
            //   },
            //   "size":"1024x1024",
            //   "quality":"high",
            //   "background":"opaque",
            //   "output_format":"png"
            // }
            // image_edit.completed:
            // {
            //     "type": "image_edit.completed",
            //     "b64_json": "...",
            //     "created_at": 1761116646,
            //     "usage": {
            //         "input_tokens": 227,
            //         "output_tokens": 4460,
            //         "total_tokens": 4687,
            //         "input_tokens_details": {
            //             "text_tokens": 33,
            //             "image_tokens": 194
            //         }
            //     },
            //     "size": "1024x1024",
            //     "quality": "high",
            //     "background": "opaque",
            //     "output_format": "png"
            // }
            else if (eventType == "image_generation.completed" || eventType == "image_edit.completed")
            {
                string b64Json = root.GetProperty("b64_json").GetString()!;
                string outputFormat = root.GetProperty("output_format").GetString()!;
                string contentType = GetContentTypeFromOutputFormat(outputFormat);

                JsonElement usageElement = root.GetProperty("usage");
                Dtos.ChatTokenUsage usage = new()
                {
                    InputTokens = usageElement.GetProperty("input_tokens").GetInt32(),
                    OutputTokens = usageElement.GetProperty("output_tokens").GetInt32(),
                    ReasoningTokens = 0,
                };

                // Yield final complete image with usage
                yield return new ChatSegment()
                {
                    FinishReason = ChatFinishReason.Stop,
                    Items = [ChatSegmentItem.FromBase64Image(b64Json, contentType)],
                    Usage = usage,
                };
            }

            eventIndex++;
        }
        Console.WriteLine($"SSE logs saved to: {logDir}");
    }

    private static async void ThrowErrorResponse(PipelineResponse response, CancellationToken cancellationToken)
    {
        // Handle error response
        Console.WriteLine($"Error response - Status: {response.Status}");
        Console.WriteLine($"Reason: {response.ReasonPhrase}");

        if (response.ContentStream != null)
        {
            using StreamReader reader = new(response.ContentStream);
            string errorContent = await reader.ReadToEndAsync(cancellationToken);
            Console.WriteLine($"Error content: {errorContent}");
            throw new Exception($"Request failed with status {response.Status}: {errorContent}");
        }
        else
        {
            throw new Exception($"Request failed with status {response.Status}: {response.ReasonPhrase}");
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "FromClientResult")]
    static extern GeneratedImageCollection FromClientResult(GeneratedImageCollection _, ClientResult result);

    protected override void SetReasoningEffort(ChatCompletionOptions options, DBReasoningEffort reasoningEffort)
    {
        _reasoningEffort = reasoningEffort;
    }

    protected override void SetImageSize(ChatCompletionOptions options, DBKnownImageSize imageSize)
    {
        _imageSize = imageSize;
    }
}
