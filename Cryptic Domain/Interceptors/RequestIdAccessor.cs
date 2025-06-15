using Cryptic_Domain.Interfaces.Access;
using Cryptic_Domain.Interfaces.Access;

namespace Cryptic_Domain.Interceptors;

public class RequestIdAccessor : IRequestIdAccessor
{
    private static readonly AsyncLocal<string> _current = new();

    public string RequestId
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}