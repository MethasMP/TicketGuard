namespace BookingGuardian.Services
{
    public enum SystemMode { TEST, LIVE }

    public interface ISystemModeService
    {
        SystemMode CurrentMode { get; }
        void SetMode(SystemMode mode);
    }

    public class SystemModeService : ISystemModeService
    {
        private SystemMode _currentMode = SystemMode.TEST; // Default to TEST for dev convenience

        public SystemMode CurrentMode => _currentMode;

        public void SetMode(SystemMode mode)
        {
            _currentMode = mode;
        }
    }
}
