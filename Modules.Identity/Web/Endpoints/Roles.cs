using Microsoft.AspNetCore.Routing;
using Modules.System.Identity.Application.Features.Roles.Commands;
using Modules.System.Identity.Application.Features.Roles.Queries;
using Roles.System.Identity.Application.Features.Roles.Commands;
using Roles.System.Identity.Application.Features.Roles.Queries;


namespace Modules.System.Identity.Web.Endpoints;

public class Roles : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        app.MapGroup(this)
             .MapPost(RolePagedList, "pagedList")
                .MapGet(RoleGetById, "{id}")
            .MapGet(RolesLookup, "lookup", configure: x => x.RequireAuthorization())
            .MapGet(RolePermissionsTree, "{roleId}/permissions/tree", configure: x => x.RequireAuthorization())
           .MapPost(RoleCreate)
                .MapPut(RoleUpdate, "{id}")
                .MapDelete(RoleDelete, "{id}")
                .MapPatch(RoleChangeState, "{id}")
                .MapPut(UpdateRolePermissions, "{roleId:guid}/permissions")
        ;
    }
    public async Task<Results<NoContent, NotFound, BadRequest>> UpdateRolePermissions(IDispatcher sender, Guid roleId, UpdateRolePermissionsCommand command)
    {
        command.RoleId = roleId;
        await sender.Send(command);
        return TypedResults.NoContent();
    }
    public async Task<Results<Ok<RolePermissionTreeResponseDto>, BadRequest>> RolePermissionsTree(IDispatcher sender, Guid roleId)
    {
        var res = await sender.Query(new RolePermissionsTreeQuery { RoleId = roleId });
        return TypedResults.Ok(res);
    }
    public async Task RoleChangeState(IDispatcher sender, Guid id)
    {
        await sender.Send(new RoleChangeStateCommand(id));
    }

    public async Task RoleDelete(IDispatcher sender, Guid id)
    {
        await sender.Send(new RoleDeleteCommand(id));
    }
    public async Task<Results<Ok<List<RoleLookupDto>>, NotFound>> RolesLookup(IDispatcher sender, CancellationToken cancellationToken = default)
    {
        var res = await sender.Query(new RoleLookupQuery(), cancellationToken);
        return TypedResults.Ok(res);
    }

    public async Task<Results<Ok<PagedList<RolePagedListDto>>, BadRequest>> RolePagedList(IDispatcher sender, RolePagedListQuery parameter, CancellationToken cancellationToken = default)
    {
        var res = await sender.Query(parameter, cancellationToken);
        return TypedResults.Ok(res);
    }

    public async Task<Results<Ok<RoleGetByIdDto>, NotFound>> RoleGetById([FromServices] IDispatcher sender, Guid id, CancellationToken cancellationToken = default)
    {
        var res = await sender.Query(new RoleGetByIdQuery(id), cancellationToken);
        return TypedResults.Ok(res);
    }
    public async Task<Results<Created<Guid>, BadRequest>> RoleCreate(IDispatcher sender, RoleCreateCommand parameter, CancellationToken cancellationToken = default)
    {
        var res = await sender.Send(parameter, cancellationToken);
        return TypedResults.Created(res.ToString(), res);
    }
    public async Task<NoContent> RoleUpdate([FromServices] IDispatcher sender, [FromRoute] Guid id, RoleUpdateCommand parameter, CancellationToken cancellationToken = default)
    {
        parameter.Id = id;
        await sender.Send(parameter, cancellationToken);
        return TypedResults.NoContent();
    }


}