using GeekApplication.Interfaces.ContentWriterV3;

namespace GeekAPI.Services.ContentWriterV3;

public class ContentGeneratorFactory : IContentGeneratorFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ContentGeneratorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IContentGenerator Get(ContentGeneratorProvider provider) =>
        _serviceProvider.GetRequiredKeyedService<IContentGenerator>(provider);
}
