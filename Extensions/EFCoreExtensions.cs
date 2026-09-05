using Microsoft.EntityFrameworkCore;

namespace prohpharmacy_trekking_app.Extensions
{
    public static class EFCoreExtensions
    {
        /// <summary>
        /// Updates an existing entity by its id if found, otherwise inserts a new one.
        /// </summary>
        public static async Task<Entity> UpdateOrCreate<Entity>(
            this DbSet<Entity> dbSet,
            DbContext dbContext,
            Guid? id,
            Entity entity) where Entity : class
        {
            var existingEntity = await dbSet.FindAsync(id);
            if (existingEntity != null)
            {
                if (!dbSet.Local.Contains(existingEntity))
                    dbContext.Attach(existingEntity);

                var entityType = dbContext.Model.FindEntityType(typeof(Entity))!;
                var primaryKey = entityType.FindPrimaryKey()!;
                var properties = entityType.GetProperties()
                    .Where(p => !primaryKey.Properties.Contains(p));

                foreach (var property in properties)
                {
                    var propertyEntry = dbContext.Entry(existingEntity).Property(property.Name);
                    var value = propertyEntry.OriginalValue;
                    if (dbContext.Entry(entity).Property(property.Name).CurrentValue != null)
                        value = dbContext.Entry(entity).Property(property.Name).CurrentValue;

                    propertyEntry.CurrentValue = value;
                }
            }
            else
            {
                if (dbSet.Local.Contains(entity))
                    throw new InvalidOperationException("The entity is already being tracked by the context.");

                existingEntity = dbSet.Add(entity).Entity;
            }

            return existingEntity;
        }
    }
}
