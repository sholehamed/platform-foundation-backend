using Domain.SharedKernel.Common;

namespace Domain.SharedKernel.Entities
{
    public interface IFullAuditableEntity:IAuditableEntity, ISoftDelete
    {

    }
    public abstract class FullAuditableEntity : AuditableEntity, IAuditable, ISoftDelete, IFullAuditableEntity
    {
        public bool IsDeleted { get; set; }
        public Guid? DeletedBy { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string? DeletedByIp { get; set; }
        protected FullAuditableEntity()
        {

        }
        protected FullAuditableEntity(ushort code) : base(code) { }
        protected FullAuditableEntity(ulong id, ushort code) : base(id, code) { }
        protected FullAuditableEntity(Guid id) : base(id) { }

        public void Delete(Guid userId, string ip)
        {
            IsDeleted = true;
            DeletedAt = DateTime.Now;
            DeletedBy = userId;
            DeletedByIp = ip;
        }
    }
}
