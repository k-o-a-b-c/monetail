namespace Monetail.Windows.Client;

public interface ITokenStore
{
    TokenSet? Load(ApiEndpoint endpoint);
    void Save(ApiEndpoint endpoint, TokenSet tokens);
    void Clear();
}
