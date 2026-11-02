using UnityEngine;

public static class LapSaveSystem
{
    private const string KeyPrefix = "MyGameName_lapdata_";

    public static void Save(LapDataSO data)
    {
        string json = JsonUtility.ToJson(data);
        PlayerPrefs.SetString(KeyPrefix + data.trackId, json);
        PlayerPrefs.Save();
    }

    public static void Load(LapDataSO data)
    {
        string key = KeyPrefix + data.trackId;
        if (!PlayerPrefs.HasKey(key)) return;

        JsonUtility.FromJsonOverwrite(PlayerPrefs.GetString(key), data);
    }

    public static void Delete(LapDataSO data)
    {
        PlayerPrefs.DeleteKey(KeyPrefix + data.trackId);
        PlayerPrefs.Save();
    }

    public static string FormatTime(float t)
    {
        int minutes = (int)(t / 60f);
        float seconds = t - minutes * 60f;
        return $"{minutes}:{seconds:00.000}";
    }
}