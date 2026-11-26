using Xunit;

// Settings, the active-world UI snapshots and the journal are shared by the process, as they are in the game.
// A separate world fixture may publish its own snapshot, so these tests also need isolation from other collections.
[CollectionDefinition("settings", DisableParallelization = true)]
public sealed class SettingsWorldTests { }
