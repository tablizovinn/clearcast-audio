using CLEARCAST.Core.Enums;

namespace CLEARCAST.Application.Abstractions;

public interface IProcessingModeService
{
    ProcessingMode CurrentMode { get; }

    void SetMode(ProcessingMode mode);
}
