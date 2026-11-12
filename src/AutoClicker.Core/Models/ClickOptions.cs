namespace AutoClicker.Core.Models;

public enum MouseButton { Left, Right, Middle }
public enum ClickType { Single, Double }
public enum ClickMode { Toggle, Hold, MouseHold, HoldMouseButton }
public enum ClickPositionMode { CurrentCursor, Fixed }
public enum ProfileColor { Gray, Red, Orange, Yellow, Green, Teal, Blue, Purple, Pink }
public enum ApplicationLanguage { Turkish, English }
// Dark is retained as a legacy serialized value and normalized to Night on load.
public enum Appearance { System, Night, CrimsonGold, Graphite, Light, Dark, GeceNeon, SadeBeyaz, AuroraCam, Terminal, OyunHud }
public enum CloseBehavior { ExitApplication, MinimizeToTray }
public readonly record struct ScreenPoint(int X, int Y);
