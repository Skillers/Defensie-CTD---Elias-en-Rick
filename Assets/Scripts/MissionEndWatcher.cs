using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Polls <see cref="MissionSession.AllPlansFinished"/> and triggers a scene change once
/// every spawned unit has either arrived or been marked unreachable. Drop one of these on
/// a GameObject in the Game scene and set <see cref="nextScene"/> to the scene that should
/// load when the mission ends. Fires at most once per scene load.
/// </summary>
public class MissionEndWatcher : MonoBehaviour
{
    [Header("Next Scene")]
#if UNITY_EDITOR
    [SerializeField] private UnityEditor.SceneAsset nextScene;
#endif
    [SerializeField, HideInInspector] private string nextSceneName;

    [Header("Timing")]
    [Tooltip("Seconds to wait after all units finish before loading the next scene. Gives the player a moment to see arrival.")]
    [SerializeField] private float delaySeconds = 2f;

    bool _triggered;
    float _triggerTime;

#if UNITY_EDITOR
    void OnValidate()
    {
        if (nextScene != null) nextSceneName = nextScene.name;
    }
#endif

    void Update()
    {
        if (_triggered)
        {
            if (Time.time >= _triggerTime + delaySeconds)
                LoadNextScene();
            return;
        }

        if (MissionSession.Instance == null) return;
        if (!MissionSession.Instance.AllPlansFinished) return;

        _triggered   = true;
        _triggerTime = Time.time;
    }

    void LoadNextScene()
    {
        if (string.IsNullOrEmpty(nextSceneName))
        {
            Debug.LogError("MissionEndWatcher: nextSceneName is empty — cannot transition.");
            enabled = false;
            return;
        }
        SceneManager.LoadScene(nextSceneName);
    }
}
