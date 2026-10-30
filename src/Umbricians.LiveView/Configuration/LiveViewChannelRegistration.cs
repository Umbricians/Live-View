namespace Umbricians.LiveView.Configuration;

/// <summary>
/// A channel declared in code with <see cref="Extensions.LiveViewBuilderExtensions.AddLiveViewChannel"/>. Its
/// values fill whatever appsettings leaves empty, ahead of the built-in defaults.
/// </summary>
internal sealed record LiveViewChannelRegistration(string Alias, LiveViewChannelOptions Defaults);
