using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Azure;
using Azure.AI.OpenAI;
using System.Text;

class Program
{
    // This example requires environment variables named "OPEN_AI_KEY" and "OPEN_AI_ENDPOINT"
    // Your endpoint should look like the following https://YOUR_OPEN_AI_RESOURCE_NAME.openai.azure.com/
    static string openAIKey = Environment.GetEnvironmentVariable("OPEN_AI_KEY");
    static string openAIEndpoint = Environment.GetEnvironmentVariable("OPEN_AI_ENDPOINT");

    // Enter the deployment name you chose when you deployed the model.
    static string engine = "gpt-4-32k-deploy";

    // This example requires environment variables named "SPEECH_KEY" and "SPEECH_REGION"
    static string speechKey = Environment.GetEnvironmentVariable("SPEECH_KEY");
    static string speechRegion = Environment.GetEnvironmentVariable("SPEECH_REGION");

    // Sentence end symbols for splitting the response into sentences.
    static List<string> sentenceSaperators = new() { ".", "!", "?", ";", "。", "！", "？", "；", "\n" };

    private static object consoleLock = new();

    // Prompts Azure OpenAI with a request and synthesizes the response.
    async static Task AskOpenAI(string prompt)
    {
        var speechConfig = SpeechConfig.FromSubscription(speechKey, speechRegion);
        // The language of the voice that speaks.
        speechConfig.SpeechSynthesisVoiceName = "ja-JP-NanamiNeural";
        var audioOutputConfig = AudioConfig.FromDefaultSpeakerOutput();
        using var speechSynthesizer = new SpeechSynthesizer(speechConfig, audioOutputConfig);
        // speechSynthesizer.Synthesizing += (sender, args) =>
        // {
        //     lock (consoleLock)
        //     {
        //         Console.ForegroundColor = ConsoleColor.Yellow;
        //         Console.Write($"[Audio]");
        //         Console.ResetColor();
        //     }
        // };

        // Ask Azure OpenAI
        OpenAIClient client = new(new Uri(openAIEndpoint), new AzureKeyCredential(openAIKey));
        var completionsOptions = new ChatCompletionsOptions()
        {
            DeploymentName = engine,
            Messages = {
                new ChatMessage(ChatRole.User, prompt)
            },
            MaxTokens = 500,
        };
        var gptBuffer = new StringBuilder();
        await foreach (StreamingChatCompletionsUpdate chatUpdate in client.GetChatCompletionsStreaming(completionsOptions))
        {
            if (chatUpdate.Role.HasValue)
            {
                var text = chatUpdate.Role.Value.ToString().ToUpperInvariant();
                Console.Write($"{text}: ");
            }
            if (!string.IsNullOrEmpty(chatUpdate.ContentUpdate))
            {
                Console.Write(chatUpdate.ContentUpdate);
                gptBuffer.Append(chatUpdate.ContentUpdate);
            }
        }
        Console.WriteLine();

        await speechSynthesizer.SpeakTextAsync(gptBuffer.ToString()).ConfigureAwait(true);
        gptBuffer.Clear();
    }

    // Continuously listens for speech input to recognize and send as text to Azure OpenAI
    async static Task ChatWithOpenAI()
    {
        // Should be the locale for the speaker's language.
        var speechConfig = SpeechConfig.FromSubscription(speechKey, speechRegion);
        speechConfig.SpeechRecognitionLanguage = "ja-JP";

        using var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
        using var speechRecognizer = new SpeechRecognizer(speechConfig, audioConfig);
        var conversationEnded = false;

        while (!conversationEnded)
        {
            Console.WriteLine("Azure OpenAI is listening. Say 'Stop' or press Ctrl-Z to end the conversation.");

            // Get audio from the microphone and then send it to the TTS service.
            var speechRecognitionResult = await speechRecognizer.RecognizeOnceAsync();

            switch (speechRecognitionResult.Reason)
            {
                case ResultReason.RecognizedSpeech:
                    if (speechRecognitionResult.Text == "Stop.")
                    {
                        Console.WriteLine("Conversation ended.");
                        conversationEnded = true;
                    }
                    else
                    {
                        Console.WriteLine($"Recognized speech: {speechRecognitionResult.Text}");
                        await AskOpenAI(speechRecognitionResult.Text).ConfigureAwait(true);
                    }
                    break;
                case ResultReason.NoMatch:
                    Console.WriteLine($"No speech could be recognized: ");
                    break;
                case ResultReason.Canceled:
                    var cancellationDetails = CancellationDetails.FromResult(speechRecognitionResult);
                    Console.WriteLine($"Speech Recognition canceled: {cancellationDetails.Reason}");
                    if (cancellationDetails.Reason == CancellationReason.Error)
                    {
                        Console.WriteLine($"Error details={cancellationDetails.ErrorDetails}");
                    }
                    break;
            }
        }
    }

    async static Task Main(string[] args)
    {
        try
        {
            await ChatWithOpenAI().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}