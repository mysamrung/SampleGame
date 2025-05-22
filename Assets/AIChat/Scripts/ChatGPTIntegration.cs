using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;


namespace AIChat
{
    public class ChatGPTIntegration : MonoBehaviour
    {
        // UI Elements (Optional: Display the response in UI)
        public TMP_InputField userInputField;
        public TextMeshProUGUI responseText;

        // OpenAI API URL and Key
        private string apiUrl = "https://api.openai.com/v1/chat/completions";
        private string apiKey = "";  // Replace with your API key

        // Send message to ChatGPT
        public void SendMessageToChatGPT()
        {
            string userMessage = userInputField.text;

            string worldSetting = "You are in a medieval fantasy world. Magic exists, knights protect kingdoms, and dragons are real. The world is filled with mysteries, ancient ruins, and adventurers. You are a young wizard, learning spells and exploring dungeons, trying to uncover hidden secrets.";
            string characterContext = "You are a young wizard who has just started your journey. YouÅfre eager to learn new spells and face challenges, but you're still learning about the magic around you.";
            string toneSetting = "The world is mysterious, but light-hearted, with a sense of wonder and discovery.";

            string prompt = worldSetting + "\n" + characterContext + "\n" + toneSetting + "\nChatGPT:";
            StartCoroutine(SendRequestToAPI(prompt, userMessage));
        }

        // Make an HTTP request to OpenAI API
        private IEnumerator SendRequestToAPI(string prompt, string userMessage)
        {
            // Set up the headers
            UnityWebRequest request = new UnityWebRequest(apiUrl, "POST");
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            // Prepare the data for the POST request
            // Build the prompt using these settings and the user's message
            // Create the JSON request body using the corrected structure for chat completions
            string jsonData = "{\"model\":\"gpt-4\",\"messages\":[{\"role\":\"system\",\"content\":\"" + prompt + "\"}, {\"role\":\"user\",\"content\":\"" + userMessage + "\"}]}";



            // Convert data to a byte array
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            // Send the request and wait for a response
            yield return request.SendWebRequest();

            // Check for errors
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Request failed: " + request.error);
                responseText.text = "Error: " + request.error;
            }
            else
            {
                // Parse the JSON response
                string jsonResponse = request.downloadHandler.text;
                ChatGPTResponse response = JsonUtility.FromJson<ChatGPTResponse>(jsonResponse);

                // Display the response in the UI
                responseText.text = response.choices[0].message.content;
            }
        }

        // Response object to map the OpenAI JSON response
        [System.Serializable]
        public class ChatGPTResponse
        {
            public Choice[] choices;
        }

        [System.Serializable]
        public class Choice
        {
            public Message message;
        }

        [System.Serializable]
        public class Message
        {
            public string content;
        }
    }
}