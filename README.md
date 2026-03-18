# Redisqa

**Redisqa** is an application for automated deployment of a relational-like architecture on top of the Redis key–value store.

It allows you to design ERD (Entity–Relationship Diagram) schemas similar to classic relational databases and then use that architecture directly inside Redis.

## How it works

The core idea is based on native Redis data structures:

- **Table rows** are stored as **Hashes**
- **Indexes** and **relationships** are stored as **Sets**

This approach does not inherit the heavy, complex mechanisms of the full relational model, but it provides a familiar relational abstraction layer over an in-memory key–value store. Like SQL-style models, it offers a convenient way to work with structured data — while benefiting from Redis performance.

## Screenshots
<img width="1710" height="1069" alt="Снимок экрана 2026-03-18 в 15 43 38" src="https://github.com/user-attachments/assets/6cfae47b-3265-4e7c-8cb8-7fd1575df67c" />
<img width="1709" height="1039" alt="Снимок экрана 2026-03-18 в 15 44 41" src="https://github.com/user-attachments/assets/9da71389-fa18-4853-821c-630ed17373e3" />
