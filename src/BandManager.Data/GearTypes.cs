namespace BandManager.Data;

/// <summary>
/// The gear-type picklist plus each type's starting Settings template -
/// e.g. picking "Amplifier" pre-populates GearSetting rows named Gain/
/// Bass/Mid/Treble/... with blank values for the user to fill in.
///
/// This is deliberately NOT a live web lookup: there's no public
/// database of "the correct settings" for a given amplifier - that's
/// subjective, tutorial-style content (forums, YouTube), not structured
/// data any API returns. So "lookup" here means "know what KINDS of
/// settings this category of gear usually has," not "know the right
/// values for this exact model" - the user fills in and edits every
/// value themselves, same as GearSetting.cs's doc comment.
/// </summary>
public static class GearTypes
{
    public static readonly IReadOnlyDictionary<string, string[]> DefaultSettings = new Dictionary<string, string[]>
    {
        ["Amplifier"] = ["Gain", "Bass", "Mid", "Treble", "Presence", "Reverb", "Master Volume", "Channel"],
        ["Guitar/Bass"] = ["Tuning", "String Gauge", "Pickup Selector"],
        ["Effects Pedal"] = ["Level", "Tone", "Gain/Drive"],
        ["Multi-Effects Unit"] = ["Preset/Patch", "Input Level", "Output Level"],
        ["Microphone"] = ["Gain", "Pad", "Polar Pattern", "High-Pass Filter"],
        ["Mixer/Sound Board"] = ["Channel", "Gain", "EQ", "Aux Send"],
        ["Drum Kit/Percussion"] = ["Tuning", "Head Type"],
        ["Keyboard/Synth"] = ["Patch/Voice", "Octave", "Sustain"],
        ["In-Ear Monitor/Wireless"] = ["Frequency", "Channel", "Volume"],
        ["Cable"] = [],
        ["Case/Bag"] = [],
        ["Clothing"] = [],
        ["Jewelry/Accessory"] = [],
        ["Pick"] = [],
        ["Strap"] = [],
        ["Stand/Mount"] = [],
        ["Other"] = []
    };

    public static readonly string[] AllTypes = DefaultSettings.Keys.ToArray();
}
