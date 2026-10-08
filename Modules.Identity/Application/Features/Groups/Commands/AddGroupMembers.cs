using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Groups.Commands
{
    public record AddGroupMembersCommand : ICommand
    {
        public Guid? GroupId { get; set; }
        public List<Guid> UserIds { get; set; }
    }
    public class AddGroupMembersCommandHandler : ICommandHandler<AddGroupMembersCommand>
    {
        private IIdentityDbContext _context;

        public AddGroupMembersCommandHandler(IIdentityDbContext context)
        {
            _context = context;
        }

        public async Task Handle(AddGroupMembersCommand command, CancellationToken cancellationToken)
        {
            List<UserGroup> _userGroupsToAdd = command.UserIds.Select(x => new UserGroup { UserId = x, GroupId = command.GroupId!.Value }).ToList();
            _context.UserGroups.AddRange(_userGroupsToAdd);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
