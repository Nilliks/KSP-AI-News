using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>
    /// The player's side of the conversation: comments and Snackboard posts as the agency or anonymously,
    /// the community's reactions to them, ask-me-anything threads, and press photos.
    /// </summary>
    public partial class NewsEngine
    {
        bool communityBusy, imageBusy;
        double communityRetryReal;
        readonly Queue<Story> imageQueue = new Queue<Story>();
        /// <summary>Writes a generated picture to disk under the given file name. Null in tests that don't care.</summary>
        public Action<string, byte[]> SaveImage;
        public string CommunityStatus = "";
        public string ImageStatus = "";

        public string AgencyName { get { return State.Profile.Name != "" ? State.Profile.Name : "the agency"; } }

        // ------------------------------------------------------------ posting

        /// <summary>Adds the player's comment to an article ("a:id") or Snackboard thread ("t:id") and asks the community to react.</summary>
        public Comment PlayerComment(string target, string text, bool asAgency, string handle, string replyTo, double ut)
        {
            text = (text ?? "").Trim();
            var list = CommentsOf(target);
            if (text == "" || list == null) return null;
            EnsureIds(list);
            var c = new Comment
            {
                Id = State.NewId("c"), Text = ResponseParser.Clip(text, 2000), UT = ut, Player = true, Agency = asAgency,
                Author = asAgency ? AgencyName : AnonName(handle), Likes = 1, Vote = 1,
                ReplyTo = list.Any(x => x.Id == replyTo) ? replyTo : "",
            };
            list.Add(c);
            TownHallAnswered(ThreadOf(target), c);
            Queue(new CommunityJob { Target = target, CommentId = c.Id, Mode = "react", Count = ReplyCount(target, asAgency), UT = ut });
            return c;
        }

        /// <summary>Starts a Snackboard thread. An AMA asks the community for that many questions for the player to answer.</summary>
        public ForumThread PlayerThread(string board, string title, string body, bool asAgency, string handle, bool ama, int questions, double ut)
        {
            title = (title ?? "").Trim();
            if (title == "") return null;
            if (Array.IndexOf(ResponseParser.Boards, board) < 0) board = ama ? "ama" : "general";
            var t = new ForumThread
            {
                Id = State.NewId("t"), Board = board, Title = ResponseParser.Clip(title, 200), Body = ResponseParser.Clip((body ?? "").Trim(), 4000),
                Author = asAgency ? AgencyName : AnonName(handle), UT = ut, Player = true, Agency = asAgency, Score = 1, Vote = 1,
                Kind = ama ? "ama" : "post",
            };
            State.Forum.Add(t);
            Queue(new CommunityJob { Target = "t:" + t.Id, Mode = ama ? "ama" : "react", Count = ama ? Math.Max(1, Math.Min(20, questions)) : ReplyCount("t:" + t.Id, asAgency), UT = ut });
            return t;
        }

        string AnonName(string handle)
        {
            handle = (handle ?? "").Trim().TrimStart('@');
            if (handle == "") handle = State.AnonHandle != "" ? State.AnonHandle : "orbital_onlooker";
            State.AnonHandle = ResponseParser.Clip(handle, 30);
            return State.AnonHandle;
        }

        void Queue(CommunityJob j)
        {
            State.CommunityJobs.Add(j);
            CommunityStatus = "Waiting for replies...";
        }

        /// <summary>Bigger stories and official accounts draw more replies.</summary>
        int ReplyCount(string target, bool asAgency)
        {
            double sig = 30;
            var a = ArticleOf(target);
            if (a != null) sig = a.Significance;
            int n = sig >= 60 ? 5 : sig >= 35 ? 4 : 3;
            return asAgency ? n + 1 : n;
        }

        public void Vote(string target, string commentId, int dir)
        {
            dir = Math.Sign(dir);
            if (commentId == "")
            {
                var t = ThreadOf(target);
                if (t == null) return;
                int old = t.Vote;
                t.Vote = t.Vote == dir ? 0 : dir;
                t.Score += t.Vote - old;
                return;
            }
            var list = CommentsOf(target);
            var c = list != null ? list.FirstOrDefault(x => x.Id == commentId) : null;
            if (c == null) return;
            int was = c.Vote;
            c.Vote = c.Vote == dir ? 0 : dir;
            c.Likes += c.Vote - was;
        }

        public Article ArticleOf(string target) { return target.StartsWith("a:") ? State.Articles.FirstOrDefault(a => a.Id == target.Substring(2)) : null; }
        public ForumThread ThreadOf(string target) { return target.StartsWith("t:") ? State.Forum.FirstOrDefault(t => t.Id == target.Substring(2)) : null; }

        public List<Comment> CommentsOf(string target)
        {
            var a = ArticleOf(target);
            if (a != null) return a.Comments;
            var t = ThreadOf(target);
            if (t != null) return t.Comments;
            var g = ShotOf(target);
            return g != null && g.Public ? g.Comments : null;
        }

        public void EnsureIds(List<Comment> list)
        {
            foreach (var c in list) if (c.Id == "") c.Id = State.NewId("c");
        }

        // ------------------------------------------------------------ community replies

        /// <summary>The one outlet a comment job is about ("" for forum threads), so the prompt carries only that outlet.</summary>
        string CommunityOutlet(CommunityJob job)
        {
            if (!job.Target.StartsWith("a:")) return "";
            var a = State.Articles.FirstOrDefault(x => x.Id == job.Target.Substring(2));
            return a != null ? a.Outlet : null;
        }

        /// <summary>One community request at a time, independent of the news queue. Player-triggered, so it is the only extra AI use.</summary>
        void TickCommunity(double ut, double realTime, bool allowCalls)
        {
            if (communityBusy || State.CommunityJobs.Count == 0 || realTime < communityRetryReal) return;
            var job = State.CommunityJobs[0];
            var list = CommentsOf(job.Target);
            if (list == null) { State.CommunityJobs.RemoveAt(0); return; }
            if (!Settings.AiWrites(GlobalSettings.AiMost))
            {
                ApplyReplies(job, OfflineWriter.CommunityReplies(this, job), ut);
                return;
            }
            if (!allowCalls) return;
            communityBusy = true;
            job.Attempts++;
            CommunityStatus = "The community is typing...";
            var route = Settings.Cheap;
            var preset = route.Preset;
            // A released photo nobody has described yet: a model that can see gets the picture itself.
            var photo = ShotOf(job.Target);
            byte[] jpeg = photo != null && photo.Look == "" && Sees(route) ? JpegOf(photo) : null;
            string cuser = PromptBuilder.Community(this, job, Clock, ut) + (jpeg != null ? "\nThe photo itself is attached: people react to what they can actually see in it." : "");
            var req = Providers.Build(preset, route.BaseUrl, route.Key, route.Model, PromptBuilder.System(State, Settings.ModsText, CommunityOutlet(job)), cuser, Math.Min(Settings.MaxTokens, 3000), preset.JsonMode, jpeg);
            req.TimeoutSeconds = Settings.TimeoutSeconds;
            Transport.Send(req, res =>
            {
                communityBusy = false;
                if (jpeg != null && Providers.RefusedImage(res)) { Providers.MarkBlind(preset, route.Model); return; }   // the job stays queued and goes again without the picture
                // A reload may have replaced the save: only apply to a job that still exists in it.
                var live = State.CommunityJobs.FirstOrDefault(j => j.Target == job.Target && j.CommentId == job.CommentId && j.UT == job.UT);
                if (live == null) return;
                string text, err;
                List<Comment> replies = null;
                int playerLikes = -1;
                if (Providers.TryExtractText(preset.Kind, res, out text, out err))
                {
                    object root;
                    var json = ResponseParser.ExtractJson(text);
                    if (json != null && Json.TryParse(json, out root))
                    {
                        var known = new HashSet<string>((CommentsOf(live.Target) ?? new List<Comment>()).Select(c => c.Id));
                        replies = ResponseParser.ParseReplies(Json.Arr(Json.Get(root, "replies")), known, 25);
                        playerLikes = (int)Json.Num(root, "player_likes", -1);
                    }
                    else err = "Reply was not JSON";
                }
                if (replies != null && replies.Count > 0)
                {
                    ApplyReplies(live, replies, ut, playerLikes);
                    return;
                }
                LastError = err ?? "No usable replies";
                if (live.Attempts >= MaxAttempts || IsPermanent(res)) ApplyReplies(live, OfflineWriter.CommunityReplies(this, live), ut);
                else communityRetryReal = realTime + 15 * live.Attempts;
            });
        }

        void ApplyReplies(CommunityJob job, List<Comment> replies, double ut, int playerLikes = -1)
        {
            State.CommunityJobs.Remove(job);
            var list = CommentsOf(job.Target);
            if (list == null) return;
            var ids = new List<string>();
            foreach (var r in replies)
            {
                r.Id = State.NewId("c");
                ids.Add(r.Id);
                r.UT = ut;
                if (r.ReplyTo.StartsWith("#"))
                {
                    int i;
                    r.ReplyTo = int.TryParse(r.ReplyTo.Substring(1), out i) && i < ids.Count ? ids[i] : "";
                }
                else if (r.ReplyTo == "" && job.CommentId != "" && job.Mode == "react" && ids.Count == 1) r.ReplyTo = job.CommentId;
                list.Add(r);
            }
            var mine = list.FirstOrDefault(c => c.Id == job.CommentId);
            if (mine != null && playerLikes >= 0) mine.Likes = Math.Max(mine.Likes, playerLikes);
            var thread = ThreadOf(job.Target);
            if (thread != null && job.CommentId == "" && playerLikes >= 0) thread.Score = Math.Max(thread.Score, playerLikes);
            CommunityStatus = replies.Count + (replies.Count == 1 ? " reply" : " replies") + " came in.";
            if (CommunityChanged != null) CommunityChanged();
        }

        public event Action CommunityChanged;

        // ------------------------------------------------------------ press photos

        bool WantsImage(Story story, CoveragePlan plan)
        {
            return Settings.ImagesOn && story.Kind != "filler" && ImageProviders.TierQualifies(plan.Tier, Settings.ImageMinTier);
        }

        void QueueImage(Story story, string prompt)
        {
            story.ImagePrompt = prompt != "" ? prompt : PromptBuilder.FallbackImagePrompt(story, State);
            imageQueue.Enqueue(story);
        }

        void TickImages(bool allowCalls)
        {
            if (imageBusy || imageQueue.Count == 0 || !allowCalls || !Settings.ImagesOn) return;
            var story = imageQueue.Dequeue();
            var preset = Settings.ImagePreset;
            var req = ImageProviders.Build(preset, Settings.ImageBaseUrl, Settings.ImageModel, Settings.ImageKey, story.ImagePrompt);
            imageBusy = true;
            ImageStatus = "Developing a press photo...";
            Transport.Send(req, res =>
            {
                imageBusy = false;
                byte[] bytes;
                string err;
                if (!ImageProviders.TryExtract(preset.Kind, res, out bytes, out err)) { ImageStatus = "Photo failed: " + err; return; }
                // Unique name: after a revert, article ids can be reused for different news.
                string file = story.Id + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ImageProviders.Extension(bytes);
                var arts = State.Articles.Where(a => a.StoryId == story.Id && a.Image == "").ToList();
                if (arts.Count == 0) { ImageStatus = "Photo arrived for news that was rolled back."; return; }
                if (SaveImage != null) SaveImage(file, bytes);
                foreach (var a in arts) a.Image = file;
                ImageStatus = "Photo published.";
                if (CommunityChanged != null) CommunityChanged();
            });
        }

        /// <summary>Generates one sample photo so the player can check the image settings.</summary>
        public void TestImage(Action<bool, string, string> done)
        {
            var preset = Settings.ImagePreset;
            if (preset == null) { done(false, "Pick an image service first.", ""); return; }
            if (!Settings.ImagesOn) { done(false, "Enter an API key and model for " + preset.Name + " first.", ""); return; }
            var req = ImageProviders.Build(preset, Settings.ImageBaseUrl, Settings.ImageModel, Settings.ImageKey,
                "A tall white rocket stands on the launch pad at dawn, steam venting, with a small crowd of kerbals watching from behind a fence in the foreground.");
            Transport.Send(req, res =>
            {
                byte[] bytes;
                string err;
                if (!ImageProviders.TryExtract(preset.Kind, res, out bytes, out err)) { done(false, err, ""); return; }
                string file = "test_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ImageProviders.Extension(bytes);
                if (SaveImage != null) SaveImage(file, bytes);
                done(true, "Got a " + (bytes.Length / 1024) + " KB picture from " + preset.Name + " (" + Settings.ImageModel + ").", file);
            });
        }
    }
}
