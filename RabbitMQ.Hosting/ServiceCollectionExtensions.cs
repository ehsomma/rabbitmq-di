using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RabbitMQ.Client;
using RabbitMQ.Core;
using RabbitMQ.Hosting;
using System;
using System.Reflection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods para registrar consumers RabbitMQ en DI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra toda la infraestructura necesaria para consumir eventos de integración desde RabbitMQ.
    /// </summary>
    /// <param name="services">Colección de servicios DI.</param>
    /// <param name="handlersAssembly">Assembly donde se encuentran los handlers de eventos de integración (clases que implementan <see cref="IIntegrationMessageHandler"/>).</param>
    /// <param name="configure">Acción que inicializa <see cref="RabbitOptions"/>.</param>
    public static IServiceCollection AddRabbitMqIntegrationConsumer(
        this IServiceCollection services,
        Assembly handlersAssembly,
        Action<RabbitOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // =====================================================
        // Registra RabbitOptions.
        // Configuración de RabbitMQ.
        // =====================================================
        RabbitOptions options = new RabbitOptions();
        configure(options);
        services.AddSingleton(options);

        // =====================================================
        // Registra los handlers de los eventos de integración.
        //
        // Opción A: uno por uno (manual).
        // Opción B: con Scrutor (scan por assembly).
        //
        // En este caso usamos Scrutor para no registrar handler por handler.
        // Busca clases en el assembly de THandlerAssemblyMarker que implementen
        // IIntegrationMessageHandler y las registra como sus interfaces.
        //
        // Resultado:
        // DI podrá resolver IEnumerable<IIntegrationMessageHandler> con todos
        // los handlers encontrados.
        // =====================================================
        services.Scan(scan => scan
            .FromAssemblies(handlersAssembly)
            .AddClasses(c => c.AssignableTo<IIntegrationMessageHandler>())
            .AsImplementedInterfaces()
            .WithSingletonLifetime());

        // =====================================================
        // Registra el IntegrationEventTypeResolver.
        //
        // Convierte el string recibido en BasicProperties.Type al Type CLR del evento.
        // =====================================================
        services.AddSingleton<IntegrationEventTypeResolver>();

        // =====================================================
        // Registra el IntegrationEventDispatcher.
        //
        // El dispatcher recibe internamente:
        // IEnumerable<IIntegrationMessageHandler>
        //
        // DI construye automáticamente ese IEnumerable con todos los handlers
        // registrados arriba.
        // =====================================================
        services.AddSingleton<IntegrationEventDispatcher>();

        // =====================================================
        // RabbitMQ
        //
        // Analogía de conceptos básicos:
        // 📞 Channel = Línea telefónica con el correo (línea de comunicación con RabbitMQ).
        // 🏢 Exchange = Centro de clasificación de correo (distribuidor de mensajes).
        // 📬 Queue = Buzón donde quedan los mensajes luego de clasificarlos.
        // 🔗 Bind = Etiqueta que le dice al centro: "Todo lo que diga 'Ventas' mandalo al buzón Ventas.".
        // =====================================================

        // Registra ConnectionFactory.
        services.AddSingleton(sp =>
        {
            RabbitOptions opt = sp.GetRequiredService<RabbitOptions>();

            return new ConnectionFactory
            {
                HostName = opt.HostName,
                UserName = opt.UserName,
                Password = opt.Password
            };
        });

        // Registra Connection.
        // [!] IMPORTANTE:
        // Crea la conexión async, pero como estamos en composición DI (sync),
        // usa "sync over async" con GetAwaiter().GetResult().
        // En Worker está OK porque ocurre una sola vez durante startup.
        services.AddSingleton<IConnection>(sp =>
        {
            ConnectionFactory connectionFactory = sp.GetRequiredService<ConnectionFactory>();
            return connectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
        });

        // Registra el inicializador de topología RabbitMQ.
        services.AddSingleton<RabbitMqTopologyInitializer>();

        // Registra el Worker genérico.
        //
        // AddHostedService<T> le dice al Host:
        // "cuando la aplicación arranque, ejecutá este BackgroundService".
        services.AddHostedService<RabbitMqConsumerWorker>();

        return services;
    }

    /// <summary>
    /// Registra la infraestructura mínima necesaria para publicar eventos
    /// de integración en RabbitMQ.
    /// </summary>
    /// <param name="services">Colección de servicios DI.</param>
    /// <param name="configure">Acción que inicializa <see cref="RabbitOptions"/>.</param>
    public static IServiceCollection AddRabbitMqPublisher(
        this IServiceCollection services,
        Action<RabbitOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        RabbitOptions options = new RabbitOptions();
        configure(options);

        services.TryAddSingleton(options);

        // =====================================================
        // RabbitMQ
        //
        // Analogía de conceptos básicos:
        // 📞 Channel = Línea telefónica con el correo (línea de comunicación con RabbitMQ).
        // 🏢 Exchange = Centro de clasificación de correo (distribuidor de mensajes).
        // 📬 Queue = Buzón donde quedan los mensajes luego de clasificarlos.
        // 🔗 Bind = Etiqueta que le dice al centro: "Todo lo que diga 'Ventas' mandalo al buzón Ventas.".
        // =====================================================

        // Registra ConnectionFactory.
        services.TryAddSingleton(sp =>
        {
            RabbitOptions opt = sp.GetRequiredService<RabbitOptions>();

            return new ConnectionFactory
            {
                HostName = opt.HostName,
                UserName = opt.UserName,
                Password = opt.Password
            };
        });

        // Registra Connection.
        // [!] IMPORTANTE:
        // Crea la conexión async, pero como estamos en composición DI (sync),
        // usa "sync over async" con GetAwaiter().GetResult().
        // En Worker / Console está OK porque ocurre una sola vez durante startup.
        services.TryAddSingleton<IConnection>(sp =>
        {
            ConnectionFactory connectionFactory = sp.GetRequiredService<ConnectionFactory>();
            return connectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
        });

        // Registra el publicador raw.
        services.TryAddSingleton<IIntegrationEventPublisher, RabbitMqIntegrationEventPublisher>();

        return services;
    }

    /// <summary>
    /// Registra un publicador de eventos de integración basado en convención.
    /// </summary>
    /// <param name="services">Colección de servicios DI.</param>
    /// <param name="configure">Acción que inicializa <see cref="RabbitOptions"/>.</param>
    public static IServiceCollection AddRabbitMqConventionPublisher(
        this IServiceCollection services,
        Action<RabbitOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddRabbitMqPublisher(configure);

        services.TryAddSingleton<IIntegrationEventNamingStrategy, DefaultIntegrationEventNamingStrategy>();
        services.TryAddSingleton<IConventionIntegrationEventPublisher, ConventionIntegrationEventPublisher>();

        return services;
    }
}
