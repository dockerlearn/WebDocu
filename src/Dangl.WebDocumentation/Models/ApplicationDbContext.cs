﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Identity.EntityFramework;
using Microsoft.Data.Entity;

namespace Dangl.WebDocumentation.Models
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {

        public DbSet<DocumentationProject> DocumentationProjects { get; set; }

        public DbSet<UserProjectAccess> UserProjects { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<DocumentationProject>()
                .HasIndex(Entity => Entity.Name)
                .IsUnique();

            builder.Entity<DocumentationProject>()
                .HasIndex(Entity => Entity.ApiKey)
                .IsUnique();

            //builder.Entity<DocumentationProject>()
            //    .HasKey(Entity => Entity.Id);

            builder.Entity<UserProjectAccess>()
                .HasKey(Entity => new {Entity.ProjectId, Entity.UserId});

            builder.Entity<UserProjectAccess>()
                .HasOne(Entity => Entity.Project)
                .WithMany(Project => Project.UserAccess)
                .OnDelete(Microsoft.Data.Entity.Metadata.DeleteBehavior.Cascade);

            // Customize the ASP.NET Identity model and override the defaults if needed.
            // For example, you can rename the ASP.NET Identity table names and more.
            // Add your customizations after calling base.OnModelCreating(builder);
        }
    }
}
