using Microsoft.Extensions.DependencyInjection;

namespace Underground;

public sealed record HandlerDescriptor(Type MessageHandler);