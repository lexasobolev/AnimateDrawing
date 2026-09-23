using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AnimatedDrawingsWorld.Core
{
    [RequireComponent(typeof(CharacterStateController))]
    public class AiAnimationController : MonoBehaviour
    {
        private const int MaxSteps = 16;
        private const float MinStepDuration = 0.1f;
        private const float MaxStepDuration = 10f;

        [Header("AI provider")]
        [SerializeField] private string endpoint = "https://api.openai.com/v1/chat/completions";
        [SerializeField] private string apiKey;
        [SerializeField] private string model = "gpt-4o-mini";
        [SerializeField, TextArea(2, 4)] private string prompt = "Персонаж радостно машет рукой, затем удивляется.";
        [SerializeField] private bool playOnStart;

        private CharacterStateController stateController;
        private Coroutine activeRequest;

        private void Awake()
        {
            stateController = GetComponent<CharacterStateController>();
        }

        private void Start()
        {
            if (playOnStart) GenerateFromPrompt();
        }

        [ContextMenu("Generate animation from Prompt")]
        public void GenerateFromPrompt()
        {
            GenerateFromPrompt(prompt);
        }

        [ContextMenu("Play test animation (no AI request)")]
        public void PlayTestAnimation()
        {
            PlayPlan(new AiAnimationPlan
            {
                steps = new[]
                {
                    new AiAnimationStep { state = "Wave", duration = 1.5f },
                    new AiAnimationStep { state = "Surprised", duration = 1f },
                    new AiAnimationStep { state = "Idle", duration = 1f }
                }
            });
        }

        public void GenerateFromPrompt(string userPrompt)
        {
            if (string.IsNullOrWhiteSpace(userPrompt))
            {
                Debug.LogWarning($"{name}: AI animation prompt is empty.", this);
                return;
            }

            Debug.Log($"{name}: sending AI animation prompt: {userPrompt}", this);
            if (activeRequest != null) StopCoroutine(activeRequest);
            activeRequest = StartCoroutine(RequestPlan(userPrompt));
        }

        public void PlayPlan(AiAnimationPlan plan)
        {
            if (!TryValidatePlan(plan, out var error))
            {
                Debug.LogWarning($"{name}: AI animation plan rejected: {error}", this);
                return;
            }

            if (activeRequest != null) StopCoroutine(activeRequest);
            activeRequest = StartCoroutine(PlayPlanRoutine(plan));
            Debug.Log($"{name}: accepted AI animation plan with {plan.steps.Length} steps.", this);
        }

        private IEnumerator RequestPlan(string userPrompt)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Debug.LogWarning($"{name}: assign an API key before requesting an AI animation plan.", this);
                yield break;
            }

            var requestBody = new ChatCompletionRequest
            {
                model = model,
                temperature = 0.2f,
                messages = new[]
                {
                    new ChatMessage { role = "system", content = SystemPrompt },
                    new ChatMessage { role = "user", content = userPrompt }
                },
                response_format = new ResponseFormat { type = "json_object" }
            };

            using var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(requestBody))),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            yield return request.SendWebRequest();

            Debug.Log($"{name}: AI response HTTP {(long)request.responseCode} ({request.result}).", this);

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"{name}: AI response body: {request.downloadHandler.text}", this);
                Debug.LogWarning($"{name}: AI animation request failed ({request.responseCode}): {request.error}", this);
                yield break;
            }

            Debug.Log($"{name}: AI raw response: {request.downloadHandler.text}", this);

            ChatCompletionResponse response;
            try
            {
                response = JsonUtility.FromJson<ChatCompletionResponse>(request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{name}: AI response is not valid JSON: {exception.Message}", this);
                yield break;
            }

            var content = response?.choices != null && response.choices.Length > 0
                ? response.choices[0].message?.content
                : null;
            if (string.IsNullOrWhiteSpace(content))
            {
                Debug.LogWarning($"{name}: AI response did not contain an animation plan.", this);
                yield break;
            }

            AiAnimationPlan plan;
            try
            {
                plan = JsonUtility.FromJson<AiAnimationPlan>(RemoveMarkdownFence(content));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{name}: AI animation plan is not valid JSON: {exception.Message}", this);
                yield break;
            }

            Debug.Log($"{name}: AI animation content: {content}", this);
            PlayPlan(plan);
        }

        private IEnumerator PlayPlanRoutine(AiAnimationPlan plan)
        {
            foreach (var step in plan.steps)
            {
                var state = (BehaviorState)Enum.Parse(typeof(BehaviorState), step.state, true);
                Debug.Log($"{name}: playing AI step {state} for {step.duration:0.##} seconds.", this);
                stateController.Interrupt(state, step.duration);
                yield return new WaitForSeconds(step.duration);
            }

            activeRequest = null;
        }

        private static bool TryValidatePlan(AiAnimationPlan plan, out string error)
        {
            if (plan?.steps == null || plan.steps.Length == 0)
            {
                error = "the plan has no steps";
                return false;
            }

            if (plan.steps.Length > MaxSteps)
            {
                error = $"the plan contains more than {MaxSteps} steps";
                return false;
            }

            foreach (var step in plan.steps)
            {
                if (step == null || !Enum.TryParse(step.state, true, out BehaviorState state))
                {
                    error = $"unknown state '{step?.state}'";
                    return false;
                }

                if (step.duration < MinStepDuration || step.duration > MaxStepDuration)
                {
                    error = $"duration for {state} must be between {MinStepDuration} and {MaxStepDuration} seconds";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private static string RemoveMarkdownFence(string content)
        {
            var result = content.Trim();
            if (!result.StartsWith("```", StringComparison.Ordinal)) return result;

            var firstLineEnd = result.IndexOf('\n');
            var lastFence = result.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineEnd < 0 || lastFence <= firstLineEnd) return result;
            return result.Substring(firstLineEnd + 1, lastFence - firstLineEnd - 1).Trim();
        }

        private const string SystemPrompt =
            "Return only a JSON object with this exact shape: " +
            "{\"steps\":[{\"state\":\"Wave\",\"duration\":1.2}]}. " +
            "Allowed states: Idle, Walk, Yawn, Sleep, Wave, Surprised, Dance. " +
            "Use 1 to 16 steps. Every duration must be between 0.1 and 10 seconds. " +
            "Translate the user's animation request into a short sequence.";

        [Serializable]
        private class ChatCompletionRequest
        {
            public string model;
            public float temperature;
            public ChatMessage[] messages;
            public ResponseFormat response_format;
        }

        [Serializable]
        private class ChatMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class ResponseFormat
        {
            public string type;
        }

        [Serializable]
        private class ChatCompletionResponse
        {
            public ChatChoice[] choices;
        }

        [Serializable]
        private class ChatChoice
        {
            public ChatMessage message;
        }
    }
}
