
namespace VVardenfell.Runtime.Bootstrap
{
    public enum BootstrapAudioPhase : byte
    {
        None = 0,
        IntroCompany = 1,
        Loading = 2,
        IntroLogo = 3,
        Menu = 4,
        Dismissed = 5,
    }

    public static class BootstrapPresentationAudioState
    {
        public static BootstrapAudioPhase CurrentPhase { get; private set; }

        public static void Reset()
        {
            CurrentPhase = BootstrapAudioPhase.None;
        }

        public static void SetPhase(BootstrapAudioPhase phase)
        {
            CurrentPhase = phase;
        }
    }
}
