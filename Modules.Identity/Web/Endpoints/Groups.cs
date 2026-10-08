using Groups.System.Identity.Application.Features.Groups.Commands;
using Groups.System.Identity.Application.Features.Groups.Queries;
using Modules.System.Identity.Application.Features.Groups.Commands;
using Modules.System.Identity.Application.Features.Groups.Queries;
using Modules.System.Identity.Application.Features.Users.Queries;

namespace Groups.System.Identity.Web.Endpoints
{
    public class Groups : EndpointGroupBase
    {
        public override void Map(WebApplication app)
        {
            app.MapGroup(this)

                .MapPost(GroupPagedList, "pagedList")
                .MapGet(GroupGetById, "{id}")
                .MapPost(GroupMembers, "{groupId:guid}/members")
                .MapGet(GroupLookup, "lookup")
                .MapGet(SearchUserToAddGroup, "searchmembers")
                .MapPost(GroupCreate)
                .MapPut(GroupUpdate, "{id}")
                .MapDelete(GroupDelete, "{id}")
                .MapPatch(GroupChangeState, "{id}")
                .MapPost(AddGroupMembers, "{GroupId}/addmembers")
            ;
        }
        public async Task<NoContent> AddGroupMembers(IDispatcher sender,Guid GroupId, AddGroupMembersCommand parameter, CancellationToken cancellationToken)
        {
            parameter.GroupId = GroupId; 
            await sender.Send(parameter, cancellationToken);
            return TypedResults.NoContent();
        }
        public async Task<Results<Ok<PagedList<GroupMemberPagedListDto>>, NotFound>> GroupMembers(IDispatcher sender, Guid groupId, GroupMembersPagedListQuery parameter, CancellationToken cancellationToken)
        {
            parameter.GroupId = groupId;
            var res = await sender.Query(parameter, cancellationToken);
            return TypedResults.Ok(res);
        }
        public async Task<Results<Ok<List<UserPagedListDto>>, NotFound>> SearchUserToAddGroup(IDispatcher sender, [AsParameters] SearchUserToAddGroupQuery parameter, CancellationToken cancellationToken)
        {
            var res = await sender.Query(parameter, cancellationToken);
            return TypedResults.Ok(res);
        }
        public async Task GroupChangeState(IDispatcher sender, Guid id)
        {
            await sender.Send(new GroupChangeStateCommand(id));
        }

        public async Task GroupDelete(IDispatcher sender, Guid id)
        {
            await sender.Send(new GroupDeleteCommand(id));
        }
        public async Task<Results<Ok<List<LookupDto>>, BadRequest>> GroupLookup(IDispatcher sender, string? text, CancellationToken cancellationToken = default)
        {
            var res = await sender.Query(new GroupLookupQuery(text), cancellationToken);
            return TypedResults.Ok(res);
        }
        public async Task<Results<Ok<PagedList<GroupPagedListDto>>, BadRequest>> GroupPagedList(IDispatcher sender, GroupPagedListQuery parameter, CancellationToken cancellationToken = default)
        {
            var res = await sender.Query(parameter, cancellationToken);
            return TypedResults.Ok(res);
        }

        public async Task<Results<Ok<GroupGetByIdDto>, NotFound>> GroupGetById([FromServices] IDispatcher sender, Guid id, CancellationToken cancellationToken = default)
        {
            var res = await sender.Query(new GroupGetByIdQuery(id), cancellationToken);
            return TypedResults.Ok(res);
        }
        public async Task<Results<Created<Guid>, BadRequest>> GroupCreate(IDispatcher sender, GroupCreateCommand parameter, CancellationToken cancellationToken = default)
        {
            var res = await sender.Send(parameter, cancellationToken);
            return TypedResults.Created(res.ToString(), res);
        }
        public async Task<NoContent> GroupUpdate([FromServices] IDispatcher sender, [FromRoute] Guid id, GroupUpdateCommand parameter, CancellationToken cancellationToken = default)
        {
            parameter.Id = id;
            await sender.Send(parameter, cancellationToken);
            return TypedResults.NoContent();
        }
    }
}
