using System.Threading.Tasks;
using Pulumi;

class Program
{
    static Task Main() => Deployment.RunAsync<Stack>();
}