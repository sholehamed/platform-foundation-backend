using Domain.SharedKernel.Common;
using Domain.SharedKernel.Entities;

namespace Modules.System.Identity.Domain.Entities
{
    public class User : AuditableEntity, IMustHaveTenant
    {
        public User() : base(17) { }
        public User(ulong id) : base(id, 17) { }
        public User(Guid id) : base(id) { }
        public User(Guid tenantId, string userName, string email,  string? firstName, string? lastName, bool isActive=true) : base(17)
        {
            TenantId = tenantId;
            UserName = userName;
            Email = email;
          
            FirstName = firstName;
            LastName = lastName;
            IsActive = isActive;
           
        }
        public User(Guid id,Guid tenantId, string userName, string email, string? firstName, string? lastName, bool isActive = true) : base(id)
        {
            TenantId = tenantId;
            UserName = userName;
            Email = email;

            FirstName = firstName;
            LastName = lastName;
            IsActive = isActive;

        }
        public Guid? ImageId { get; set; }
        public Guid TenantId { get; set; }

        public string UserName { get; set; } = default!;
        public string NormalizedUserName { get; set; } = default!;

        public string Email { get; set; } = default!;
        public string NormalizedEmail { get; set; } = default!;
        public bool EmailConfirmed { get; set; }

        public string PasswordHash { get; set; } = default!;
        public string? SecurityStamp { get; set; }
        public string? ConcurrencyStamp { get; set; }

        public string? FirstName { get; set; }
        public string? LastName { get; set; }

        public bool IsActive { get; set; } = true;
        public bool IsLockedOut { get; set; }
        public DateTime? LockoutEndUtc { get; set; }

        public int AccessFailedCount { get; set; }
        public virtual Tenant? Tenant { get; set; }
        public ICollection<UserRole> UserRoles { get; set; } = [];
        public ICollection<UserGroup> UserGroups { get; set; } = [];
        public ICollection<UserPermission> UserPermissions { get; set; } = [];
    }

}
