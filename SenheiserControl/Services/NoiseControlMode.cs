namespace SenheiserControl.Services;

/// <summary>UI-level mode, not sent directly - derived from (and translated into) AncEnabled + AncAdaptiveEnabled.</summary>
public enum NoiseControlMode
{
    Off,
    Custom,
    Adaptive
}
