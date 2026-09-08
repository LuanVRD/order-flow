# Arquitetura do OrderFlow

## 1. Princípios e Diretrizes

O **OrderFlow** adota os princípios de **Clean Architecture** (Arquitetura Limpa) e **Domain-Driven Design (DDD) tático**, com foco no desacoplamento entre regras de negócio, infraestrutura de persistência/mensageria e interfaces de entrada.

### 1.1 Regras de Dependência (Clean Architecture)

A direção das dependências no projeto respeita rigorosamente a regra das camadas internas não conhecerem as camadas externas:

```text
       [ Domain ]  <--- (sem dependências de outros projetos da solução)
           ^
           |
      [ Application ]  <--- (depende apenas do Domain e Messaging.Contracts)
           ^
           |
    [ Infrastructure ] <--- (depende de Application, Domain e Messaging.Contracts)
           ^
           |
     [ API / Worker ]  <--- (depende de Infrastructure e Application para composição)
```

1. **Domain (`OrderFlow.*.Domain`)**: Contém entidades, objetos de valor, exceções de domínio e enums. Não possui dependências de bibliotecas de infraestrutura (como Entity Framework Core ou RabbitMQ) nem de outros projetos da solução.
2. **Application (`OrderFlow.*.Application`)**: Contém a orquestração de casos de uso (Commands/Queries), DTOs, validadores e interfaces de repositório e serviços de mensageria.
3. **Infrastructure (`OrderFlow.*.Infrastructure`)**: Implementa as interfaces da camada Application (acesso a banco PostgreSQL via EF Core, publicação/consumo RabbitMQ, etc.).
4. **API / Worker (`OrderFlow.*.Api` e `OrderFlow.*.Worker`)**: Pontos de entrada executáveis responsável pela injeção de dependências (DI), inicialização da aplicação, middlewares e exposição de endpoints ou workers de segundo plano.
5. **BuildingBlocks (`OrderFlow.Messaging.Contracts`)**: Biblioteca compartilhada contendo apenas os contratos de DTOs/Eventos de integração que trafegam entre microsserviços.

---

## 2. Microsserviços e Fronteiras de Contexto (Bounded Contexts)

### 2.1 Orders Service
- **Responsabilidade**: Gerenciar o ciclo de vida dos pedidos (`Pending` -> `Processing` -> `Completed` ou `Cancelled`).
- **Persistência**: Banco isolado `orderflow_orders` no PostgreSQL.
- **Mensageria**: Publica eventos de integração (`OrderCreated`, `OrderStatusChanged`, `OrderCompleted`, `OrderCancelled`) no RabbitMQ após mudanças de estado.

### 2.2 Notifications Service
- **Responsabilidade**: Consumir eventos publicados pelo *Orders Service*, converter em notificações auditáveis e garantir idempotência.
- **Persistência**: Banco isolado `orderflow_notifications` no PostgreSQL (tabelas `Notifications` e `ProcessedMessages`).
- **Mensageria**: Consome da fila `orderflow.notifications` com suporte a políticas de retry e Dead Letter Queue (DLQ).

---

## 3. Comunicação Assíncrona e Eventos

### 3.1 Domain Events vs. Integration Events

O OrderFlow estabelece uma separação clara entre **Domain Events** (eventos internos ao domínio) e **Integration Events** (eventos de integração entre microsserviços):

| Característica | Domain Event (`OrderFlow.*.Domain.Events`) | Integration Event (`OrderFlow.Messaging.Contracts.Events`) |
| :--- | :--- | :--- |
| **Escopo** | Intra-processo, restrito ao mesmo Bounded Context (ex.: dentro do microsserviço de Orders). | Inter-processo / Distribuído, compartilhado entre múltiplos Bounded Contexts (Orders -> Notifications). |
| **Acoplamento** | Acoplado aos tipos e entidades do Domínio (`OrderStatus`, etc.). Não deve sair do domínio. | 100% desacoplado de entidades de domínio. Utiliza apenas tipos primitivos/escalares e DTOs imutáveis. |
| **Transporte** | Disparado e tratado em memória (síncrono ou assíncrono local) dentro da mesma transação/Unit of Work. | Serializado (JSON) e publicado assincronamente através de um message broker (RabbitMQ). |
| **Finalidade** | Notificar outras partes do mesmo agregado/domínio sobre regras de negócio que mudaram de estado. | Notificar outros sistemas/microsserviços sobre fatos consumados de interesse corporativo. |
| **Versionamento** | Raro/desnecessário (refatorado junto com o código da aplicação). | Obrigatório e explícito (`version: 1`), pois múltiplos consumidores externos dependem do contrato estável. |
| **Rastreabilidade** | Contextual à execução da thread/tarefa corrente. | Exige envelope com metadados distribuídos (`EventId`, `CorrelationId`, `OccurredAt`). |

### 3.2 Estrutura do Envelope de Integração (`EventEnvelope<T>`)

Para garantir interoperabilidade e rastreabilidade entre microsserviços sem compartilhar modelos de dados complexos, todas as mensagens transitam encapsuladas em um envelope padronizado:

```json
{
  "eventId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "eventType": "OrderCreated",
  "occurredAt": "2026-08-18T12:00:00Z",
  "version": 1,
  "correlationId": "req-987654321",
  "data": {
    "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "customerName": "John Doe",
    "customerEmail": "john.doe@example.com",
    "totalAmount": 150.00,
    "status": "Pending",
    "createdAt": "2026-08-18T12:00:00Z"
  }
}
```

- **`EventId`**: Identificador único global do evento para controle de deduplicação e idempotência no consumidor.
- **`EventType`**: String descritiva da intenção/evento para roteamento e serialização polimórfica.
- **`OccurredAt`**: Timestamp UTC do momento em que o evento ocorreu.
- **`Version`**: Versão do contrato de dados (versão inicial: `1`).
- **`CorrelationId`**: Identificador de rastreamento ponta a ponta (Tracing distribuído).
- **`Data`**: Payload específico do evento com dados enxutos e estritamente necessários.

### 3.3 Contratos de Eventos de Integração Disponíveis

- **`OrderCreatedIntegrationEvent`**: `(Guid OrderId, string CustomerName, string CustomerEmail, decimal TotalAmount, string Status, DateTimeOffset CreatedAt)`
- **`OrderStatusChangedIntegrationEvent`**: `(Guid OrderId, string PreviousStatus, string NewStatus, DateTimeOffset ChangedAt)`
- **`OrderCompletedIntegrationEvent`**: `(Guid OrderId, DateTimeOffset CompletedAt)`
- **`OrderCancelledIntegrationEvent`**: `(Guid OrderId, string PreviousStatus, DateTimeOffset CancelledAt, string? Reason = null)`

### 3.4 Topologia RabbitMQ no Orders Service

O microsserviço de Orders publica eventos através da implementação `RabbitMqEventPublisher` (na camada `OrderFlow.Orders.Infrastructure`), atendendo à abstração `IEventPublisher` da camada Application.

- **Exchange**: `orderflow.orders`
  - **Tipo**: `topic`
  - **Durabilidade**: `durable: true`, `autoDelete: false`
- **Routing Keys**:
  - `order.created`: Publicado imediatamente após a criação do pedido ser persistida com sucesso.
  - `order.status.changed`: Publicado a cada transição de status do pedido.
  - `order.completed`: Publicado quando o pedido atinge o estado final `Completed`.
  - `order.cancelled`: Publicado quando o pedido é cancelado (`Cancelled`).
- **Propriedades da Mensagem AMQP**:
  - `ContentType`: `application/json`
  - `ContentEncoding`: `utf-8`
  - `DeliveryMode`: `Persistent` (2)
  - `MessageId`: `EventEnvelope.EventId` (UUID)
  - `CorrelationId`: `EventEnvelope.CorrelationId`
  - `Type`: Nome do evento (`OrderCreated`, etc.)
  - `Timestamp`: Unix Epoch do `OccurredAt`

---

## 4. Padrões Distribuídos e Limitações Técnicas

### 4.1 Limitação Técnica: Ausência de Transactional Outbox (Dual-Write Problem)

> [!WARNING]
> **Limitação Técnica Atual**: O microsserviço de Orders publica mensagens no RabbitMQ de forma síncrona diretamente nos casos de uso após a persistência no banco de dados (`SaveChangesAsync`).
> 
> **Impacto Arquitetural**:
> 1. **Dual-Write Problem**: Como a escrita no PostgreSQL e a publicação no RabbitMQ não compartilham uma transação atômica distribuída (2PC / XA), há um ponto de falha onde o pedido pode ser gravado com sucesso no banco, mas a publicação no broker falhar (ex.: indisponibilidade transitória de rede, reinício do broker).
> 2. **Semântica de Entrega**: Atualmente opera em regime de *melhor esforço* (*at-most-once* para publicação), o que pode acarretar em mensagens perdidas em caso de indisponibilidade no momento do disparo.
> 
> **Evolução Arquitetural Planejada**:
> Em etapas subsequentes de maturidade da plataforma, essa limitação será mitigada com a implementação do **Transactional Outbox Pattern**:
> - O caso de uso gravará o agregado `Order` e o registro do evento na tabela `OutboxMessages` dentro da **mesma transação relacional local** do PostgreSQL.
> - Um processo em background (*BackgroundService* / Worker ou CDC com Debezium) fará o pooling/polling e a publicação garantida no RabbitMQ com confirmações (*publisher confirms*), assegurando semântica *at-least-once* ponta a ponta.

### 4.2 Idempotência
- O consumidor consulta e registra o `eventId` na tabela `ProcessedMessages` antes de processar, evitando duplicidade de efeitos colaterais em reentregas.

### 4.3 Tratamento de Erros, Política de Retry e Dead Letter Queue (DLQ)

O consumidor `OrderEventsConsumer` adota uma política de resiliência ativa para garantir que falhas não causem travamento da fila nem perda de mensagens:

1. **Classificação de Falhas**:
   - **Falhas Definitivamente Inválidas (Poison Messages)**: Mensagens com JSON malformado (`JsonException`), envelope com payload nulo ou tipos de evento desconhecidos são rejeitadas imediatamente (`BasicNackAsync(multiple: false, requeue: false)`). O RabbitMQ encaminha a mensagem diretamente para a DLQ sem desperdício de tentativas de retry.
   - **Falhas Transitórias**: Exceções durante o processamento do caso de uso (ex.: indisponibilidade temporária do banco, concorrência, timeouts) acionam a política de retry.

2. **Política de Retry**:
   - **Tentativas Máximas**: 3 tentativas (`MaxRetryAttempts = 3`).
   - **Backoff Exponencial**: Intervalo progressivo entre tentativas (`InitialRetryDelayMs = 500ms`, `attempt 2 = 1000ms`, `attempt 3 = 2000ms`).
   - **Logs Estruturados**: Cada tentativa emite log contendo `Attempt {Current}/{Max}`, `EventType`, `EventId` e `CorrelationId`.

3. **Exaustão e Dead-Lettering**:
   - Se todas as 3 tentativas falharem, o consumidor emite log de erro crítico informando a exaustão e executa `BasicNackAsync(multiple: false, requeue: false)`.
   - O RabbitMQ captura a rejeição sem requeue através da configuração nativa de fila (`x-dead-letter-exchange: orderflow.notifications.dlx` e `x-dead-letter-routing-key: orderflow.notifications.dlq`) e move a mensagem para a fila `orderflow.notifications.dlq`, preservando todo o payload e injetando o array de headers `x-death`.

4. **Garantia de Confirmação (At-Least-Once Delivery)**:
   - A mensagem **nunca** é confirmada (`BasicAckAsync`) antes de ser processada e persistida com sucesso (ou deduplicada via idempotência).
   - Não há possibilidade de perda silenciosa nem de loop infinito de reprocessamento.

---

### 4.4 Rastreabilidade Distribuída (Correlation ID)

O fluxo de rastreabilidade ponta a ponta correlaciona qualquer requisição HTTP externa com a publicação e o consumo de eventos no broker:

1. **Header HTTP (`X-Correlation-ID`)**:
   - O `CorrelationIdMiddleware` no Orders API inspeciona o header `X-Correlation-ID`. Se ausente ou em branco, gera um novo identificador UUID.
   - O identificador é inserido no cabeçalho da resposta HTTP (`X-Correlation-ID`) e associado ao `ICorrelationContextAccessor`.
   - O middleware abre um escopo de log estruturado (`ILogger.BeginScope`) com a propriedade `CorrelationId`, garantindo que todos os logs gerados durante o request contenham o identificador.

2. **Propagação para Mensageria**:
   - Os Use Cases repassam o Correlation ID para o `EventEnvelope<T>`.
   - O `RabbitMqEventPublisher` injeta o identificador na propriedade `BasicProperties.CorrelationId` e nos headers AMQP (`X-Correlation-ID`, `correlationId`), emitindo log estruturado com `[CorrelationId: {CorrelationId}]`.

3. **Consumo e Escopo no Notifications Worker**:
   - O `OrderEventsConsumer` recupera o `CorrelationId` das propriedades da mensagem, dos headers AMQP ou do payload do envelope JSON.
   - Um escopo de log (`_logger.BeginScope`) é aberto com a chave `CorrelationId`, assegurando que todos os logs de consumo, retries, encaminhamento para DLQ e persistência de notificações compartilhem o mesmo identificador da requisição original.

---

## 5. Observabilidade e Logs Estruturados (Serilog)

O OrderFlow adota **Serilog** como motor de logging unificado para todos os executáveis (`OrderFlow.Orders.Api` e `OrderFlow.Notifications.Worker`), assegurando emissão de telemetria estruturada de alta fidelidade sem interpolação de strings.

### 5.1 Diretrizes de Logging Estruturado

1. **Message Templates Sem Interpolação**:
   - **Incorreto**: `_logger.LogInformation($"Order {order.Id} created")` (gera strings opacas e descarta índices de busca nos coletores).
   - **Correto**: `_logger.LogInformation("Order {OrderId} created successfully with status '{OrderStatus}'.", order.Id, order.Status)` (preserva propriedades indexáveis no JSON).

2. **Propriedades Canônicas Globais**:
   - `ServiceName`: Nome do serviço (`Orders.Api` ou `Notifications.Worker`).
   - `Environment`: Ambiente de execução (`Development`, `Staging`, `Production`).
   - `CorrelationId`: Identificador distribuído da operação ponta a ponta.
   - `OrderId`: Identificador do pedido quando aplicável.
   - `EventId`: UUID do evento de integração.
   - `EventType`: Nome canônico do evento de integração (`OrderCreated`, etc.).
   - `DeliveryTag`: Identificador da mensagem no canal AMQP.

3. **Filtragem de Ruído (Noise Reduction)**:
   - Overrides nos arquivos de configuração reduzem logs verbosos dos frameworks `Microsoft`, `System` e `Microsoft.AspNetCore` para `Warning`, mantendo `Microsoft.Hosting.Lifetime` e logs da aplicação em `Information`.
   - `UseSerilogRequestLogging` substitui as múltiplas linhas padrão do ASP.NET Core por um único log conciso por requisição HTTP.

4. **Segurança e Sanitização de Dados Sensíveis**:
   - Senhas, credenciais RabbitMQ e tokens de autenticação são estritamente omitidos dos logs de conexão.
   - Não são despejados payloads brutos inteiros com dados sensíveis de clientes; apenas identificadores operacionais (`OrderId`, `CustomerEmail`, `TotalAmount`, `Status`) são registrados.

---

### 5.2 Saída para Containers (JSON Estruturado)

Em ambientes de contêineres (Docker/Kubernetes), a flag `Serilog:UseJsonConsole=true` (ou execução fora de `Development`) ativa o `CompactJsonFormatter` (`clef`), permitindo que coletores como Fluentbit, Promtail, Vector e Logstash ingiram logs sem necessidade de parsing regex frágil.

Em ambiente local de desenvolvimento, a saída padrão utiliza template textual colorido e legível:
```text
[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}
```

---

### 5.3 Exemplos de Logs Estruturados em JSON (`CompactJsonFormatter`)

#### 1. Requisição HTTP de Criação de Pedido (Orders API)
```json
{
  "@t": "2026-09-08T10:15:30.1234567Z",
  "@mt": "Handling order creation request for customer '{CustomerEmail}' with total amount {TotalAmount}.",
  "@l": "Information",
  "CustomerEmail": "cliente@orderflow.com",
  "TotalAmount": 250.00,
  "CorrelationId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "ServiceName": "Orders.Api",
  "Environment": "Production"
}
```

#### 2. Publicação de Evento de Integração no RabbitMQ
```json
{
  "@t": "2026-09-08T10:15:30.2451234Z",
  "@mt": "Integration event '{EventType}' successfully published to exchange '{Exchange}' with routing key '{RoutingKey}' [EventId: {EventId}, CorrelationId: {CorrelationId}].",
  "@l": "Information",
  "EventType": "OrderCreated",
  "Exchange": "orderflow.orders",
  "RoutingKey": "order.created",
  "EventId": "a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d",
  "CorrelationId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "ServiceName": "Orders.Api",
  "Environment": "Production"
}
```

#### 3. Conclusão da Requisição HTTP (Serilog Request Logging)
```json
{
  "@t": "2026-09-08T10:15:30.2908765Z",
  "@mt": "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms",
  "@l": "Information",
  "RequestMethod": "POST",
  "RequestPath": "/api/orders",
  "StatusCode": 201,
  "Elapsed": 45.2341,
  "CorrelationId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "ServiceName": "Orders.Api",
  "Environment": "Production"
}
```

#### 4. Consumo e Processamento de Evento (Notifications Worker)
```json
{
  "@t": "2026-09-08T10:15:30.3501200Z",
  "@mt": "Processing integration event '{EventType}' [Attempt {Attempt}/{MaxAttempts}] [EventId: {EventId}, OrderId: {OrderId}, CorrelationId: {CorrelationId}].",
  "@l": "Information",
  "EventType": "OrderCreated",
  "Attempt": 1,
  "MaxAttempts": 3,
  "EventId": "a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d",
  "OrderId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "CorrelationId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "ServiceName": "Notifications.Worker",
  "Environment": "Production"
}
```

#### 5. Falha Transitória e Retry com Backoff Exponencial
```json
{
  "@t": "2026-09-08T10:15:30.4109800Z",
  "@mt": "Transient error on attempt {Attempt}/{MaxAttempts} processing event '{EventType}' [EventId: {EventId}, OrderId: {OrderId}, CorrelationId: {CorrelationId}]. Retrying in {DelayMs}ms...",
  "@l": "Warning",
  "@x": "Npgsql.NpgsqlException: Connection timeout...",
  "Attempt": 1,
  "MaxAttempts": 3,
  "EventType": "OrderCreated",
  "EventId": "a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d",
  "OrderId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "CorrelationId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "DelayMs": 500,
  "ServiceName": "Notifications.Worker",
  "Environment": "Production"
}
```

#### 6. Exaustão de Retries e Encaminhamento para DLQ
```json
{
  "@t": "2026-09-08T10:15:33.9201400Z",
  "@mt": "Exhausted all {MaxAttempts} retry attempts for event '{EventType}' [DeliveryTag: {DeliveryTag}, EventId: {EventId}, OrderId: {OrderId}, CorrelationId: {CorrelationId}]. Forwarding to DLQ '{DlQueueName}'.",
  "@l": "Error",
  "@x": "System.TimeoutException: Database operation timed out...",
  "MaxAttempts": 3,
  "EventType": "OrderCreated",
  "DeliveryTag": 1,
  "EventId": "a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d",
  "OrderId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "CorrelationId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "DlQueueName": "orderflow.notifications.dlq",
  "ServiceName": "Notifications.Worker",
  "Environment": "Production"
}
```

#### 7. Exceção Tratada no GlobalExceptionHandler
```json
{
  "@t": "2026-09-08T10:15:35.1002200Z",
  "@mt": "Unhandled server exception occurred while processing {Method} {Path} [StatusCode: {StatusCode}, ExceptionType: {ExceptionType}]: {ErrorMessage}",
  "@l": "Error",
  "@x": "System.InvalidOperationException: Database unavailable...",
  "Method": "POST",
  "Path": "/api/orders",
  "StatusCode": 500,
  "ExceptionType": "InvalidOperationException",
  "ErrorMessage": "Database unavailable",
  "CorrelationId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "ServiceName": "Orders.Api",
  "Environment": "Production"
}
```
