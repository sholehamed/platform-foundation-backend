using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Tenants.Queries
{
    public record TenantResolveBySlugQuery(string slug) : IQuery<TenantResloveBySlugDto>;

    public record TenantResloveBySlugDto : MapFrom<Tenant>
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }
        public string Slug { get; set; }
        public string Domain { get; set; }
        public Guid? Favicon { get; set; }
        public Guid? Logo { get; set; }
        public override void Mapping(Profile profile)
        {
            profile.CreateMap<Tenant, TenantResloveBySlugDto>()
                .ForMember(x => x.Code, opt => opt.MapFrom(x => GetTenantCode(x)));
        }
        public static string GetTenantCode(Tenant tenant)
        {
            string code = tenant.Slug;
            if (!string.IsNullOrEmpty(tenant.Domain))
            {
                code=code+"-"+tenant.Domain;
            }
            return code;
        }
    }


    public class TenantResolveBySlugQueryHandler : IQueryHandler<TenantResolveBySlugQuery, TenantResloveBySlugDto>
    {
        private readonly IIdentityDbContext _context;
        private readonly IMapper _mapper;

        public TenantResolveBySlugQueryHandler(IIdentityDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<TenantResloveBySlugDto> Handle(TenantResolveBySlugQuery request, CancellationToken cancellationToken)
        {
            TenantResloveBySlugDto? tenant = null;
            string[] address = request.slug.Split('.');
            bool isSlug = address.Count() > 2;
            string? slug = address.FirstOrDefault();
            if (!isSlug)
                tenant = await _context.Tenants.IgnoreQueryFilters().Where(x => x.IsActive).Where(x => x.Domain == request.slug).ProjectTo<TenantResloveBySlugDto>(_mapper.ConfigurationProvider).FirstOrDefaultAsync();
            else
                tenant = await _context.Tenants.IgnoreQueryFilters().Where(x => x.IsActive).Where(x => x.Slug == slug).ProjectTo<TenantResloveBySlugDto>(_mapper.ConfigurationProvider).FirstOrDefaultAsync();

            if (tenant is null)
                throw new Exception("tenant is not valid");

            return tenant;
        }
    }
}
