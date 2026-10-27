using Common.Database.Repositories.Generic;
using DataAccessLayer.Entities;
using DataAccessLayer.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DataAccessLayer.Repositories.Implementations;

public class RoomRepository : GenericRepository<Room>, IRoomRepository
{
    public RoomRepository(DbSet<Room> dbSet)
        : base(dbSet)
    {
    }
}
