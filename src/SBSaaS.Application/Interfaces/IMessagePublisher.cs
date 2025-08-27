using System.Threading.Tasks;

namespace SBSaaS.Application.Interfaces
{
    public interface IMessagePublisher
    {
        Task Publish<T>(T message) where T : class;
    }
}
