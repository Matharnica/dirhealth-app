# Clean Architecture

## Core Principle: Dependency Direction

Dependencies point inward. Business logic doesn't know about databases, HTTP, or frameworks.

```
[HTTP / CLI]  →  [Use Cases]  →  [Domain]
[DB / APIs]   →  [Use Cases]
                    ↑
             Interfaces defined here,
             implemented at outer layer
```

## Layer Responsibilities

**Domain** — pure business logic, no I/O:
```typescript
class Order {
  addItem(product: Product, qty: number): void {
    if (qty <= 0) throw new InvalidQuantityError(qty);
    this.items.push({ product, qty });
  }
  get total(): Money { return this.items.reduce(/* ... */); }
}
```

**Use Cases** — orchestrate domain + I/O via interfaces:
```typescript
class PlaceOrderUseCase {
  constructor(
    private readonly orders: OrderRepository,   // interface
    private readonly payments: PaymentGateway,  // interface
  ) {}

  async execute(cmd: PlaceOrderCommand): Promise<OrderId> {
    const order = Order.create(cmd);
    const payment = await this.payments.charge(order.total);
    order.markPaid(payment.id);
    return this.orders.save(order);
  }
}
```

**Infrastructure** — concrete implementations (DB, HTTP, external APIs).

## File Structure

```
src/
  domain/       ← no external imports
    order.ts
    user.ts
  use-cases/    ← imports domain only
    place-order.ts
  infrastructure/  ← implements use-case interfaces
    postgres-orders.ts
    stripe-payments.ts
  http/         ← thin: parse, call use case, respond
    orders-controller.ts
```

## Boundaries

Each boundary should answer:
- **What does it do?** (single responsibility)
- **How do you call it?** (clear interface)
- **What does it depend on?** (explicit, inward)

## When to Separate

Three modules that **change together** should probably be one module.
One module that **changes for different reasons** should be split.

## Anti-Patterns

| Avoid | Why |
|---|---|
| Business logic in controllers | Hard to test, hard to reuse |
| Direct DB calls from domain | Domain coupled to infrastructure |
| God service with 30 methods | No clear responsibility |
| Anemic domain (only getters/setters) | Logic lives scattered in services |
| Circular dependencies | Signals wrong boundary |
