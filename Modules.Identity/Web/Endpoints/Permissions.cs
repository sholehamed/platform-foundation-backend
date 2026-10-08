using Modules.System.Identity.Application.Features.Permissions.Commands;
using Modules.System.Identity.Application.Features.Permissions.Queries;

namespace Modules.System.Identity.Web.Endpoints
{
    public class Permissions : EndpointGroupBase
    {
        public override void Map(WebApplication app)
        {
            app.MapGroup(this)

                .MapPost(PermissionPagedList, "{resourceId}/pagedList")
                .MapGet(PermissionLookup, "lookup")
                .MapGet(PermissionGetById, "{id:guid}")
                .MapPost(PermissionCreate)
                .MapPut(PermissionUpdate, "{id}")
                .MapDelete(PermissionDelete, "{id}")
                .MapPatch(PermissionChangeState, "{id}")
            ;
        }
        public async Task<Ok<List<PermissionlookupDto>>> PermissionLookup(IDispatcher sender, string? text, CancellationToken ct)
        {
            var res = await sender.Query(new PermissionLookupQuery(text), ct);
            return TypedResults.Ok(res);
        }
        public async Task PermissionChangeState(IDispatcher sender, Guid id)
        {
            await sender.Send(new PermissionChangeStateCommand(id));
        }

        public async Task PermissionDelete(IDispatcher sender, Guid id)
        {
            await sender.Send(new PermissionDeleteCommand(id));
        }

        public async Task<Results<Ok<PagedList<PermissionPagedListDto>>, BadRequest>> PermissionPagedList(IDispatcher sender,Guid resourceId, PermissionPagedListQuery parameter, CancellationToken cancellationToken = default)
        {
            parameter.ResourceId=resourceId;
            var res = await sender.Query(parameter, cancellationToken);
            return TypedResults.Ok(res);
        }

        public async Task<Results<Ok<PermissionGetByIdDto>, NotFound>> PermissionGetById([FromServices] IDispatcher sender, Guid id, CancellationToken cancellationToken = default)
        {
            var res = await sender.Query(new PermissionGetByIdQuery(id), cancellationToken);
            return TypedResults.Ok(res);
        }
        public async Task<Results<Created<Guid>, BadRequest>> PermissionCreate(IDispatcher sender, PermissionCreateCommand parameter, CancellationToken cancellationToken = default)
        {
            var res = await sender.Send(parameter, cancellationToken);
            return TypedResults.Created(res.ToString(), res);
        }
        public async Task<NoContent> PermissionUpdate([FromServices] IDispatcher sender, [FromRoute] Guid id, PermissionUpdateCommand parameter, CancellationToken cancellationToken = default)
        {
            parameter.Id = id;
            await sender.Send(parameter, cancellationToken);
            return TypedResults.NoContent();
        }
    }
}
