using CLEARCAST.Application.Abstractions;
using CLEARCAST.Core.Enums;

namespace CLEARCAST.Application.Services;

public sealed class ProcessingModeService : IProcessingModeService
{
    private ProcessingMode _currentMode = ProcessingMode.Gaming;

    public ProcessingMode CurrentMode => _currentMode;

    public void SetMode(ProcessingMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unsupported processing mode.");
        }

        _currentMode = mode;
    }
}
