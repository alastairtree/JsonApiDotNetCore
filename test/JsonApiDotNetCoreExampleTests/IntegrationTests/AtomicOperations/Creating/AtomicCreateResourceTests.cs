using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Extensions;
using JsonApiDotNetCore.Serialization.Objects;
using JsonApiDotNetCoreExample;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsonApiDotNetCoreExampleTests.IntegrationTests.AtomicOperations.Creating
{
    public sealed class AtomicCreateResourceTests
        : IClassFixture<IntegrationTestContext<TestableStartup<OperationsDbContext>, OperationsDbContext>>
    {
        private readonly IntegrationTestContext<TestableStartup<OperationsDbContext>, OperationsDbContext> _testContext;
        private readonly OperationsFakers _fakers = new OperationsFakers();

        public AtomicCreateResourceTests(IntegrationTestContext<TestableStartup<OperationsDbContext>, OperationsDbContext> testContext)
        {
            _testContext = testContext;

            testContext.ConfigureServicesAfterStartup(services =>
            {
                var part = new AssemblyPart(typeof(EmptyStartup).Assembly);
                services.AddMvcCore().ConfigureApplicationPartManager(apm => apm.ApplicationParts.Add(part));
            });
        }

        [Fact]
        public async Task Can_create_resource()
        {
            // Arrange
            var newArtistName = _fakers.Performer.Generate().ArtistName;
            var newBornAt = _fakers.Performer.Generate().BornAt;

            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "performers",
                            attributes = new
                            {
                                artistName = newArtistName,
                                bornAt = newBornAt
                            }
                        }
                    }
                }
            };
            
            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<AtomicOperationsDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            responseDocument.Results.Should().HaveCount(1);
            responseDocument.Results[0].SingleData.Should().NotBeNull();
            responseDocument.Results[0].SingleData.Type.Should().Be("performers");
            responseDocument.Results[0].SingleData.Attributes["artistName"].Should().Be(newArtistName);
            responseDocument.Results[0].SingleData.Attributes["bornAt"].Should().BeCloseTo(newBornAt);
            responseDocument.Results[0].SingleData.Relationships.Should().BeNull();

            var newPerformerId = int.Parse(responseDocument.Results[0].SingleData.Id);

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var performerInDatabase = await dbContext.Performers
                    .FirstAsync(performer => performer.Id == newPerformerId);

                performerInDatabase.ArtistName.Should().Be(newArtistName);
                performerInDatabase.BornAt.Should().BeCloseTo(newBornAt);
            });
        }

        [Fact]
        public async Task Can_create_resources()
        {
            // Arrange
            const int elementCount = 5;

            var newTracks = _fakers.MusicTrack.Generate(elementCount);

            var operationElements = new List<object>(elementCount);
            for (int index = 0; index < elementCount; index++)
            {
                operationElements.Add(new
                {
                    op = "add",
                    data = new
                    {
                        type = "musicTracks",
                        attributes = new
                        {
                            title = newTracks[index].Title,
                            lengthInSeconds = newTracks[index].LengthInSeconds,
                            genre = newTracks[index].Genre,
                            releasedAt = newTracks[index].ReleasedAt
                        }
                    }
                });
            }

            var requestBody = new
            {
                atomic__operations = operationElements
            };
            
            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<AtomicOperationsDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            responseDocument.Results.Should().HaveCount(elementCount);

            for (int index = 0; index < elementCount; index++)
            {
                responseDocument.Results[index].SingleData.Should().NotBeNull();
                responseDocument.Results[index].SingleData.Type.Should().Be("musicTracks");
                responseDocument.Results[index].SingleData.Attributes["title"].Should().Be(newTracks[index].Title);
                responseDocument.Results[index].SingleData.Attributes["lengthInSeconds"].Should().BeApproximately(newTracks[index].LengthInSeconds);
                responseDocument.Results[index].SingleData.Attributes["genre"].Should().Be(newTracks[index].Genre);
                responseDocument.Results[index].SingleData.Attributes["releasedAt"].Should().BeCloseTo(newTracks[index].ReleasedAt);
                responseDocument.Results[0].SingleData.Relationships.Should().NotBeEmpty();
            }

            var newTrackIds = responseDocument.Results.Select(result => Guid.Parse(result.SingleData.Id));

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var tracksInDatabase = await dbContext.MusicTracks
                    .Where(musicTrack => newTrackIds.Contains(musicTrack.Id))
                    .ToListAsync();

                tracksInDatabase.Should().HaveCount(elementCount);

                for (int index = 0; index < elementCount; index++)
                {
                    var trackInDatabase = tracksInDatabase.Single(musicTrack => musicTrack.Id == Guid.Parse(responseDocument.Results[index].SingleData.Id));

                    trackInDatabase.Title.Should().Be(newTracks[index].Title);
                    trackInDatabase.LengthInSeconds.Should().BeApproximately(newTracks[index].LengthInSeconds, 0.00000000001M);
                    trackInDatabase.Genre.Should().Be(newTracks[index].Genre);
                    trackInDatabase.ReleasedAt.Should().BeCloseTo(newTracks[index].ReleasedAt);
                }
            });
        }

        [Fact]
        public async Task Can_create_resource_without_attributes_or_relationships()
        {
            // Arrange
            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "performers",
                            attributes = new
                            {
                            },
                            relationship = new
                            {
                            }
                        }
                    }
                }
            };
            
            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<AtomicOperationsDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            responseDocument.Results.Should().HaveCount(1);
            responseDocument.Results[0].SingleData.Should().NotBeNull();
            responseDocument.Results[0].SingleData.Type.Should().Be("performers");
            responseDocument.Results[0].SingleData.Attributes["artistName"].Should().BeNull();
            responseDocument.Results[0].SingleData.Attributes["bornAt"].Should().BeCloseTo(default(DateTimeOffset));
            responseDocument.Results[0].SingleData.Relationships.Should().BeNull();

            var newPerformerId = int.Parse(responseDocument.Results[0].SingleData.Id);

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var performerInDatabase = await dbContext.Performers
                    .FirstAsync(performer => performer.Id == newPerformerId);

                performerInDatabase.ArtistName.Should().BeNull();
                performerInDatabase.BornAt.Should().Be(default);
            });
        }

        [Fact]
        public async Task Can_create_resource_with_unknown_attribute()
        {
            // Arrange
            var newName = _fakers.Playlist.Generate().Name;

            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "playlists",
                            attributes = new
                            {
                                doesNotExist = "ignored",
                                name = newName
                            }
                        }
                    }
                }
            };
            
            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<AtomicOperationsDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            responseDocument.Results.Should().HaveCount(1);
            responseDocument.Results[0].SingleData.Should().NotBeNull();
            responseDocument.Results[0].SingleData.Type.Should().Be("playlists");
            responseDocument.Results[0].SingleData.Attributes["name"].Should().Be(newName);
            responseDocument.Results[0].SingleData.Relationships.Should().NotBeEmpty();

            var newPlaylistId = long.Parse(responseDocument.Results[0].SingleData.Id);

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var performerInDatabase = await dbContext.Playlists
                    .FirstAsync(playlist => playlist.Id == newPlaylistId);

                performerInDatabase.Name.Should().Be(newName);
            });
        }

        [Fact]
        public async Task Can_create_resource_with_unknown_relationship()
        {
            // Arrange
            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "lyrics",
                            relationships = new
                            {
                                doesNotExist = new
                                {
                                    data = new
                                    {
                                        type = "doesNotExist",
                                        id = 12345678
                                    }
                                }
                            }
                        }
                    }
                }
            };
            
            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<AtomicOperationsDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            responseDocument.Results.Should().HaveCount(1);
            responseDocument.Results[0].SingleData.Should().NotBeNull();
            responseDocument.Results[0].SingleData.Type.Should().Be("lyrics");
            responseDocument.Results[0].SingleData.Attributes.Should().NotBeEmpty();
            responseDocument.Results[0].SingleData.Relationships.Should().NotBeEmpty();

            var newLyricId = long.Parse(responseDocument.Results[0].SingleData.Id);

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var lyricInDatabase = await dbContext.Lyrics
                    .FirstAsync(lyric => lyric.Id == newLyricId);

                lyricInDatabase.Should().NotBeNull();
            });
        }

        [Fact(Skip = "TODO: @OPS: Make this test work")]
        public async Task Cannot_create_resource_with_client_generated_ID()
        {
            // Arrange
            var newTitle = _fakers.MusicTrack.Generate().Title;
            
            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "musicTracks",
                            id = Guid.NewGuid().ToString(),
                            attributes = new
                            {
                                title = newTitle
                            }
                        }
                    }
                }
            };
            
            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.Forbidden);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.Forbidden);
            responseDocument.Errors[0].Title.Should().Be("TODO: Specifying the resource ID in POST requests is not allowed.");
            responseDocument.Errors[0].Detail.Should().BeNull();
            responseDocument.Errors[0].Source.Pointer.Should().Be("/atomic:operations[0]/data/id");
        }

        [Fact]
        public async Task Cannot_create_resource_for_href_element()
        {
            // Arrange
            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        href = "/api/v1/musicTracks"
                    }
                }
            };

            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.UnprocessableEntity);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            responseDocument.Errors[0].Title.Should().Be("Failed to deserialize request body: Usage of the 'href' element is not supported.");
            responseDocument.Errors[0].Detail.Should().BeNull();
            responseDocument.Errors[0].Source.Pointer.Should().Be("/atomic:operations[0]");
        }

        [Fact]
        public async Task Cannot_create_resource_for_ref_element()
        {
            // Arrange
            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        @ref = new
                        {
                            type = "musicTracks"
                        }
                    }
                }
            };

            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.UnprocessableEntity);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            responseDocument.Errors[0].Title.Should().Be("Failed to deserialize request body: The 'ref.relationship' element is required.");
            responseDocument.Errors[0].Detail.Should().BeNull();
            responseDocument.Errors[0].Source.Pointer.Should().Be("/atomic:operations[0]");
        }

        [Fact(Skip = "TODO: @OPS: Make this test work")]
        public async Task Cannot_create_resource_for_missing_type()
        {
            // Arrange
            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            attributes = new
                            {
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<ErrorDocument>(route, requestBody);
            
            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.UnprocessableEntity);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            responseDocument.Errors[0].Title.Should().Be("Failed to deserialize request body: The 'data.type' element is required.");
            responseDocument.Errors[0].Detail.Should().BeNull();
            responseDocument.Errors[0].Source.Pointer.Should().Be("/atomic:operations[0]");
        }

        [Fact(Skip = "TODO: @OPS: Make this test work")]
        public async Task Cannot_create_resource_for_unknown_type()
        {
            // Arrange
            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "doesNotExist"
                        }
                    }
                }
            };

            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.UnprocessableEntity);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            responseDocument.Errors[0].Title.Should().Be("Failed to deserialize request body: Request body includes unknown resource type.");
            responseDocument.Errors[0].Detail.Should().Be("Resource type 'doesNotExist' does not exist.");
            responseDocument.Errors[0].Source.Pointer.Should().Be("/atomic:operations[0]");
        }
        
        [Fact(Skip = "TODO: @OPS: Make this test work")]
        public async Task Cannot_create_resource_attribute_with_blocked_capability()
        {
            // Arrange
            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "lyrics",
                            attributes = new
                            {
                                createdAt = 12.July(1980)
                            }
                        }
                    }
                }
            };
            
            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.UnprocessableEntity);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            responseDocument.Errors[0].Title.Should().Be("Failed to deserialize request body: Setting the initial value of the requested attribute is not allowed.");
            responseDocument.Errors[0].Detail.Should().Be("Setting the initial value of 'createdAt' is not allowed.");
            responseDocument.Errors[0].Source.Pointer.Should().Be("/atomic:operations[0]");
        }
        
        [Fact(Skip = "TODO: @OPS: Make this test work")]
        public async Task Cannot_create_resource_with_incompatible_attribute_value()
        {
            // Arrange
            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "performers",
                            attributes = new
                            {
                                bornAt = "not-a-valid-time"
                            }
                        }
                    }
                }
            };
            
            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.UnprocessableEntity);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            responseDocument.Errors[0].Title.Should().Be("Failed to deserialize request body.");
            responseDocument.Errors[0].Detail.Should().Be("Failed to convert 'not-a-valid-time' of type 'String' to type 'DateTimeOffset'.");
            responseDocument.Errors[0].Source.Pointer.Should().Be("/atomic:operations[0]");
        }

        [Fact]
        public async Task Can_create_resource_with_attributes_and_multiple_relationship_types()
        {
            // Arrange
            var existingLyric = _fakers.Lyric.Generate();
            var existingCompany = _fakers.RecordCompany.Generate();
            var existingPerformer = _fakers.Performer.Generate();

            var newTitle = _fakers.MusicTrack.Generate().Title;

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.AddRange(existingLyric, existingCompany, existingPerformer);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "musicTracks",
                            attributes = new
                            {
                                title = newTitle
                            },
                            relationships = new
                            {
                                lyric = new
                                {
                                    data = new
                                    {
                                        type = "lyrics",
                                        id = existingLyric.StringId
                                    }
                                },
                                ownedBy = new
                                {
                                    data = new
                                    {
                                        type = "recordCompanies",
                                        id = existingCompany.StringId
                                    }
                                },
                                performers = new
                                {
                                    data = new[]
                                    {
                                        new
                                        {
                                            type = "performers",
                                            id = existingPerformer.StringId
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
            
            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<AtomicOperationsDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            responseDocument.Results.Should().HaveCount(1);
            responseDocument.Results[0].SingleData.Should().NotBeNull();
            responseDocument.Results[0].SingleData.Type.Should().Be("musicTracks");
            responseDocument.Results[0].SingleData.Attributes["title"].Should().Be(newTitle);
            responseDocument.Results[0].SingleData.Relationships.Should().NotBeEmpty();

            var newTrackId = Guid.Parse(responseDocument.Results[0].SingleData.Id);

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var trackInDatabase = await dbContext.MusicTracks
                    .Include(musicTrack => musicTrack.Lyric)
                    .Include(musicTrack => musicTrack.OwnedBy)
                    .Include(musicTrack => musicTrack.Performers)
                    .FirstAsync(musicTrack => musicTrack.Id == newTrackId);

                trackInDatabase.Title.Should().Be(newTitle);

                trackInDatabase.Lyric.Should().NotBeNull();
                trackInDatabase.Lyric.Id.Should().Be(existingLyric.Id);

                trackInDatabase.OwnedBy.Should().NotBeNull();
                trackInDatabase.OwnedBy.Id.Should().Be(existingCompany.Id);

                trackInDatabase.Performers.Should().HaveCount(1);
                trackInDatabase.Performers[0].Id.Should().Be(existingPerformer.Id);
            });
        }
    }
}
