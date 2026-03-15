using RabbitMQ.Client;
using RabbitMQ.Core;

namespace RabbitMQ.Hosting;

/// <summary>
/// Inicializa la topología RabbitMQ del microservicio consumidor:
/// DLX, DLQ, exchange principal, queue principal, bind y QoS.
/// </summary>
public sealed class RabbitMqTopologyInitializer
{
    /// <summary>
    /// Punto de entrada principal del worker.
    /// El Host llama a este método al iniciar la aplicación.
    /// Declara toda la infraestructura RabbitMQ necesaria para el consumer.
    /// </summary>
    public async Task InitializeAsync(
        IChannel channel,
        RabbitOptions opt,
        CancellationToken cancellationToken = default)
    {
        /*
        Sobre DLQ (Dead Letter Queue):
        =============================
        Cuando el consumer captura una excepción lanzada por el *EventHandler y no puede procesar un mensaje, 
        hay 4 caminos típicos:
            A. Reintentar un número limitado de veces.
            B. Si sigue fallando (o si falla 1 sola vez) → mandarlo a DLQ (Dead Letter Queue) para análisis/manual/reproceso.
            C. Descartar (solo si el mensaje es realmente descartable).
            D. No usar DLQ y loggear el error (solo si el mensaje es realmente descartable).
        
        Un mensaje va a DLQ cuando (según config):
            • Se rechaza con BasicReject / BasicNack con requeue:false.
            • Expira por TTL.
            • Se supera el max length de la cola (si configuraste límites).

        Un DLQ por microservicio (ej: este es un microservicio de envío de emials):
            Creo que tener una DLQ por microservicio es la mejor opción para evitar mezclar mensajes de distintos 
            servicios y facilitar el análisis. En este ejemplo, el EmailService tiene su propia 
            DLQ (email.person.integration.dlq) y su propio DLX (email.person.integration.dlx) para reenviar 
            los mensajes rechazados.
        */

        // =====================================================
        // 1) Declara DLX y DLQ del microservicio.
        // =====================================================

        // DLX: Exchange donde RabbitMQ re-publica mensajes dead-lettered.
        await channel.ExchangeDeclareAsync(
            exchange: opt.DlxName,
            type: ExchangeType.Direct, // Direct es suficiente para DLQ.
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        // DLQ: Cola que recibirá los mensajes rechazados / fallidos.
        await channel.QueueDeclareAsync(
            queue: opt.DlqName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        // Bind DLQ al DLX.
        await channel.QueueBindAsync(
            queue: opt.DlqName,
            exchange: opt.DlxName,
            routingKey: opt.DlqRoutingKey,
            cancellationToken: cancellationToken);

        // =====================================================
        // 2) Define los argumentos de la queue principal para
        //    habilitar dead-lettering.
        // =====================================================

        Dictionary<string, object?> mainQueueArgs = new Dictionary<string, object?>
        {
            // Exchange al que RabbitMQ enviará el mensaje cuando se rechace.
            ["x-dead-letter-exchange"] = opt.DlxName,

            // Routing key que usará RabbitMQ al reenviarlo al DLX.
            ["x-dead-letter-routing-key"] = opt.DlqRoutingKey,
        };

        // =====================================================
        // 3) Declara exchange + queue + bind del servicio.
        // =====================================================
        //
        // [!] IMPORTANTE:
        // Si estas entidades ya existen en RabbitMQ, la declaración debe coincidir exactamente con
        // la configuración original o RabbitMQ lanzará PRECONDITION_FAILED.

        // Exchange principal de eventos de integración.
        // Exchange: Es el "distribuidor", recibe mensajes del producer y decide a qué cola(s) enviarlos según
        // reglas (tipo Direct, Topic, Fanout, etc.). 
        await channel.ExchangeDeclareAsync(
            opt.ExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        // Queue principal del microservicio.
        // Queue: Es donde se almacenan los mensajes hasta que un consumer los procesa.
        await channel.QueueDeclareAsync(
            queue: opt.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: mainQueueArgs,
            cancellationToken: cancellationToken);

        // Bind queue -> exchange mediante topic.
        // Bind: Es la regla que conecta el exchage con la queue
        await channel.QueueBindAsync(
            opt.QueueName,
            opt.ExchangeName,
            opt.BindingKey,
            cancellationToken: cancellationToken);

        // =====================================================
        // 4) Configura el QoS (Quality of Service)
        // =====================================================
        //
        // QoS controla cuántos mensajes RabbitMQ entrega "en vuelo" a este consumer antes de recibir ACKs.
        // ACK = Acknowledgement (confirmación). Es el mensaje que el consumer envía al broker para
        //       decir: "Ya procesé este mensaje correctamente.".
        //
        // - prefetchCount = 10 => hasta 10 mensajes sin ACK al mismo tiempo.
        // - Como autoAck = false, el mensaje se considera pendiente hasta ACK/NACK.
        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: opt.PrefetchCount,
            global: false,
            cancellationToken: cancellationToken);
    }
}
