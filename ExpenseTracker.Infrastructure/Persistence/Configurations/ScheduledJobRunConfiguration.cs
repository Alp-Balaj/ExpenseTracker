using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Persistence.Configurations
{
    public class ScheduledJobRunConfiguration : IEntityTypeConfiguration<ScheduledJobRun>
    {
        public void Configure(EntityTypeBuilder<ScheduledJobRun> builder)
        {
            // The job's name is its identity — there is exactly one row per job,
            // for ever. A surrogate key would allow two rows for one job, and two
            // rows for one job is two schedules.
            builder.HasKey(j => j.JobName);

            builder.Property(j => j.JobName)
                   .IsRequired()
                   .HasMaxLength(64);

            // Truncated on write: this is a log line, and an exception with a full
            // stack trace does not belong in a column every tick updates.
            builder.Property(j => j.LastResult)
                   .HasMaxLength(500);
        }
    }
}
