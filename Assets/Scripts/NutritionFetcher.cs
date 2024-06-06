using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.IO;

public class NutritionFetcher : MonoBehaviour
{
    public TextAsset labelsFile;

    private const string apiKey = "tu9FyjIuOtaB+dZ0n3bOCQ==NT0cG6y3u2kbnDly";
    private const string apiUrl = "https://api.calorieninjas.com/v1/nutrition?query=";
    public string nutritionalInfoFilePath = "Assets/Models/NutritionalInfo.txt";
    private Dictionary<string, NutritionItem> nutritionalInfoDict = new Dictionary<string, NutritionItem>();

    // Start is called before the first frame update
    void Start()
    {
        // Start fetching nutritional information
        StartCoroutine(FetchAllNutritionInfo());
    }

    // Method to fetch and store all nutritional information
    public IEnumerator FetchAllNutritionInfo()
    {
        bool fetchedFromApi = false;
        if (labelsFile == null)
        {
            Debug.LogError("Labels file is not assigned.");
            yield break;
        }

        string[] labels = labelsFile.text.Split(new char[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);

        foreach (string label in labels)
        {
            string query = label.Trim();
            string url = apiUrl + query.Replace(" ", "%20");

            using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
            {
                // Add API key to request headers
                webRequest.SetRequestHeader("X-Api-Key", apiKey);

                // Send the request
                yield return webRequest.SendWebRequest();

                if (webRequest.result == UnityWebRequest.Result.ConnectionError ||
                    webRequest.result == UnityWebRequest.Result.ProtocolError)
                {
                    Debug.LogError("Error: " + webRequest.error);
                }
                else
                {
                    // Parse JSON response
                    string jsonResponse = webRequest.downloadHandler.text;
                    NutritionResponse nutritionResponse = JsonUtility.FromJson<NutritionResponse>(jsonResponse);

                    // Extract and store relevant information
                    if (nutritionResponse != null && nutritionResponse.items != null && nutritionResponse.items.Count > 0)
                    {
                        NutritionItem nutritionItem = nutritionResponse.items[0];

                        // Store nutritional information in dictionary
                        nutritionalInfoDict[nutritionItem.name] = nutritionItem;
                        fetchedFromApi = true;
                    }
                }
            }
        }

        // If fetched from API, save nutritional information to a text file
        if (fetchedFromApi)
        {
            SaveNutritionalInfoToFile(nutritionalInfoFilePath);
        }
        else
        {
            // If fetching from API failed, try loading from file
            LoadNutritionalInfoFromFile(nutritionalInfoFilePath);
        }
    }

    // Method to save nutritional information to a text file
    private void SaveNutritionalInfoToFile(string filePath)
    {
        // Serialize nutritionalInfoDict to JSON string
        string json = JsonUtility.ToJson(nutritionalInfoDict);

        // Write JSON string to the file
        File.WriteAllText(filePath, json);
    }

    // Method to load nutritional information from a text file
    private void LoadNutritionalInfoFromFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            // Read JSON string from the file
            string json = File.ReadAllText(filePath);

            // Deserialize JSON string to nutritionalInfoDict
            nutritionalInfoDict = JsonUtility.FromJson<Dictionary<string, NutritionItem>>(json);
        }
        else
        {
            Debug.LogWarning("Nutritional information file not found: " + filePath);
        }
    }

    // Method to retrieve nutritional information for a label
    public string GetNutritionalInfo(string label)
    {
        if (nutritionalInfoDict.ContainsKey(label))
        {
            NutritionItem nutritionItem = nutritionalInfoDict[label];
            return $"Calories: {nutritionItem.calories}\nFat: {nutritionItem.fat_total_g}g\nProtein: {nutritionItem.protein_g}g\nCarbohydrates: {nutritionItem.carbohydrates_total_g}g";
        }
        else
        {
            Debug.LogWarning("Nutritional information not found for label: " + label);
            return null;
        }
    }
}

// Data structures for JSON parsing
[System.Serializable]
public class NutritionItem
{
    public string name;
    public float calories;
    public float serving_size_g;
    public float fat_total_g;
    public float fat_saturated_g;
    public float protein_g;
    public float sodium_mg;
    public float potassium_mg;
    public float cholesterol_mg;
    public float carbohydrates_total_g;
    public float fiber_g;
    public float sugar_g;
}

[System.Serializable]
public class NutritionResponse
{
    public List<NutritionItem> items;
}