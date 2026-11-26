using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class OsowiecSliderReadout : MonoBehaviour
{
    public enum Format { Percent, Multiplier, Quality, Degrees }

    [SerializeField] private Slider slider;
    [SerializeField] private TMP_Text label;
    [SerializeField] private Format format;
    private float lastValue = float.NaN;

    public void Bind(Slider source, TMP_Text target, Format notation)
    {
        slider = source;
        label = target;
        format = notation;
    }

    private void LateUpdate()
    {
        if (slider == null || label == null || slider.value == lastValue) return;
        lastValue = slider.value;
        label.text = format switch
        {
            Format.Degrees => $"{lastValue:0}°",
            Format.Multiplier => $"{lastValue:0.0}×",
            Format.Quality => QualitySettings.names.Length > 0
                ? QualitySettings.names[Mathf.Clamp((int)lastValue, 0, QualitySettings.names.Length - 1)]
                : "Стандарт",
            _ => $"{lastValue:P0}"
        };
    }
}
