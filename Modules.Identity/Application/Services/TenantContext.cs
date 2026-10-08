using Modules.System.Identity.Application.Abstractions;

namespace Modules.System.Identity.Application.Services
{
    public sealed class TenantContext : ITenantContext
    {
        private Guid[] _readable = [];
        private Guid[] _writable = [];
        private int _disableDepth;

        public Guid? UserId { get; private set; }
        public Guid? HomeTenantId { get; private set; }
        public Guid? ActiveTenantId { get; private set; }
        public bool IsSuperAdmin { get; private set; }

        public IReadOnlyCollection<Guid> ReadableTenantIds => _readable;
        public IReadOnlyCollection<Guid> WritableTenantIds => _writable;
        public bool IsFilterDisabled => _disableDepth > 0;

        // این دو برای Query Filter مصرف می‌شوند
        public Guid[] ReadScope => _readable;

        public void Initialize(Guid userId, Guid homeTenantId, bool isSuperAdmin,
                               Guid[] readable, Guid[] writable, Guid? activeTenantId)
        {
            UserId = userId;
            HomeTenantId = homeTenantId;
            IsSuperAdmin = isSuperAdmin;
            _readable = readable;
            _writable = writable;
            ActiveTenantId = activeTenantId;
        }

        public IDisposable DisableFilter()
        {
            _disableDepth++;
            return new Scope(this);
        }

        private sealed class Scope(TenantContext ctx) : IDisposable
        {
            public void Dispose() => ctx._disableDepth--;
        }
    }

}
