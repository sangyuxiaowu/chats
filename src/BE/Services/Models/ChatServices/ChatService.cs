﻿using Chats.BE.DB;
using Chats.BE.Services.Models.Dtos;
using Tokenizer = Microsoft.ML.Tokenizers.Tokenizer;
using OpenAI.Chat;
using Microsoft.ML.Tokenizers;
using Chats.BE.Services.Models.Extensions;
using Chats.BE.Services.Models.ChatServices;
using Chats.BE.DB.Enums;

namespace Chats.BE.Services.Models;

public abstract partial class ChatService : IDisposable
{
    internal protected Model Model { get; }
    internal protected Tokenizer Tokenizer { get; }

    internal static Tokenizer DefaultTokenizer { get; } = TiktokenTokenizer.CreateForEncoding("cl100k_base");

    protected static TimeSpan NetworkTimeout { get; } = TimeSpan.FromHours(24);

    public ChatService(Model model)
    {
        Model = model;
        if (model.ModelReference.Tokenizer is not null)
        {
            Tokenizer = TiktokenTokenizer.CreateForEncoding(model.ModelReference.Tokenizer.Name);
        }
        else
        {
            Tokenizer = DefaultTokenizer;
        }
    }

    public abstract IAsyncEnumerable<ChatSegment> ChatStreamed(IReadOnlyList<ChatMessage> messages, ChatCompletionOptions options, CancellationToken cancellationToken);

    public virtual async Task<ChatSegment> Chat(IReadOnlyList<ChatMessage> messages, ChatCompletionOptions options, CancellationToken cancellationToken)
    {
        List<ChatSegmentItem> segments = [];
        ChatSegment? lastSegment = null;
        await foreach (ChatSegment seg in ChatStreamed(messages, options, cancellationToken))
        {
            lastSegment = seg;
            segments.AddRange(seg.Items);
        }

        return new ChatSegment()
        {
            Usage = lastSegment?.Usage,
            FinishReason = lastSegment?.FinishReason,
            Items = segments,
        };
    }

    internal protected int GetPromptTokenCount(IReadOnlyList<ChatMessage> messages)
    {
        const int TokenPerConversation = 3;
        int messageTokens = messages.Sum(m => m.CountTokens(Tokenizer));
        return TokenPerConversation + messageTokens;
    }

    protected virtual async Task<ChatMessage[]> FEPreprocess(IReadOnlyList<ChatMessage> messages, ChatCompletionOptions options, ChatExtraDetails feOptions, CancellationToken cancellationToken)
    {
        if (Model.ModelReference.AllowSearch)
        {
            SetWebSearchEnabled(options, feOptions.WebSearchEnabled);
        }

        if (ModelReference.SupportsCodeExecution(Model.ModelReference.Name))
        {
            SetCodeExecutionEnabled(options, feOptions.CodeExecutionEnabled);
        }

        if (ModelReference.ReasoningEffortOptions(Model.ModelReference.Name).Length > 0 && feOptions.ReasoningEffort != DBReasoningEffort.Default)
        {
            SetReasoningEffort(options, feOptions.ReasoningEffort);
        }

        if (feOptions.ImageSize != DBKnownImageSize.Default)
        {
            SetImageSize(options, feOptions.ImageSize);
        }

        if (!Model.ModelReference.AllowSystemPrompt)
        {
            // Remove system prompt
            messages = [.. messages.Where(m => m is not SystemChatMessage)];
        }
        else
        {
            // system message transform
            SystemChatMessage? existingSystemPrompt = messages.OfType<SystemChatMessage>().FirstOrDefault();
            DateTime now = feOptions.Now;
            if (existingSystemPrompt is not null)
            {
                existingSystemPrompt.Content[0] = existingSystemPrompt.Content[0].Text
                    .Replace("{{CURRENT_DATE}}", now.ToString("yyyy/MM/dd"))
                    .Replace("{{MODEL_NAME}}", Model.ModelReference.DisplayName ?? Model.ModelReference.Name)
                    .Replace("{{CURRENT_TIME}}", now.ToString("HH:mm:ss"));
            }
        }

        ChatMessage[] filteredMessage = await messages
            .ToAsyncEnumerable()
            .SelectAwait(async m => await FilterVision(Model.ModelReference.AllowVision, m, cancellationToken))
            .ToArrayAsync(cancellationToken);
        options.Temperature = Model.ModelReference.UnnormalizeTemperature(options.Temperature);

        return filteredMessage;
    }

    protected virtual void SetImageSize(ChatCompletionOptions options, DBKnownImageSize imageSize)
    {
        // chat service not enable image size by default, prompt a warning
        Console.WriteLine($"{Model.ModelReference.Name} chat service not support image generation.");
    }

    protected virtual void SetWebSearchEnabled(ChatCompletionOptions options, bool enabled)
    {
        // chat service not enable search by default, prompt a warning
        Console.WriteLine($"{Model.ModelReference.Name} chat service not support web search.");
    }

    protected virtual void SetCodeExecutionEnabled(ChatCompletionOptions options, bool enabled)
    {
        // chat service not enable code execution by default, prompt a warning
        Console.WriteLine($"{Model.ModelReference.Name} chat service not support code execution.");
    }

    protected virtual void SetReasoningEffort(ChatCompletionOptions options, DBReasoningEffort reasoningEffort)
    {
        options.ReasoningEffortLevel = reasoningEffort.ToReasoningEffort();
    }

    public void Dispose()
    {
        Disposing();
        GC.SuppressFinalize(this);
    }

    protected virtual void Disposing() { }
}
