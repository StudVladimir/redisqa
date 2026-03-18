# Redisqa

**Redisqa** is an application for automated deployment of a relational-like architecture on top of the Redis key–value store.

It allows you to design ERD (Entity–Relationship Diagram) schemas similar to classic relational databases and then use that architecture directly inside Redis.

## How it works

The core idea is based on native Redis data structures:

- **Table rows** are stored as **Hashes**
- **Indexes** and **relationships** are stored as **Sets**

This approach does not inherit the heavy, complex mechanisms of the full relational model, but it provides a familiar relational abstraction layer over an in-memory key–value store. Like SQL-style models, it offers a convenient way to work with structured data — while benefiting from Redis performance.

## Screenshots
