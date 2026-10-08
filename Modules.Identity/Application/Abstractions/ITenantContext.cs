namespace Modules.System.Identity.Application.Abstractions
{
    public interface ITenantContext
    {
        Guid? UserId { get; }
        Guid? HomeTenantId { get; }              // تننت خود کاربر
        Guid? ActiveTenantId { get; }             // تننت انتخاب‌شده در UI
        bool IsSuperAdmin { get; }

        IReadOnlyCollection<Guid> ReadableTenantIds { get; }
        IReadOnlyCollection<Guid> WritableTenantIds { get; }

        bool IsFilterDisabled { get; }
        Guid[] ReadScope { get; }

        IDisposable DisableFilter();               // فقط برای Worker و Job سیستمی
        void Initialize(Guid userId, Guid homeTenantId, bool isSuperAdmin, Guid[] readable, Guid[] writable, Guid? activeTenantId);
    }

}
