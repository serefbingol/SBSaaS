namespace SBSaaS.Application.Interfaces;

public interface ICurrentUser
{
    Guid? UserId { get; }
}
