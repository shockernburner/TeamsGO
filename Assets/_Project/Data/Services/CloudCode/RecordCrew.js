// Inputs: declared in this file's Unity import settings (the .meta), which is what deployment sends. The
// module.exports.params at the bottom documents them; Unity would only read those in an initialised JS project.
// TETHER: Primal — a team's finished online match, sent by its host, checked and added to the team's totals.
// The same rules as Match/CareerScore.TeamDelta: the players' scores times a bonus for how many got out
// (1: x1.0, 2: x1.3, 3: x1.6, 4: x2.0); a wipe (nobody out) takes a quarter of the team's average match.
// The team is the host's own (Teams.js), read on the server; its entry is keyed by the team's id.
const { LeaderboardsApi } = require("@unity-services/leaderboards-1.1");
const { DataApi } = require("@unity-services/cloud-save-1.4");

const COUNTRIES = ["US","GB","CA","AU","NZ","IE","DE","FR","ES","IT","NL","BE","SE","NO","DK","FI","PL","PT","BR","MX",
  "AR","CL","RU","UA","TR","IN","PK","BD","LK","SG","MY","ID","PH","TH","VN","JP","KR","TW","HK","CN","SA","AE","EG",
  "NG","ZA"];
const MIN_SECONDS = 30, MAX_SECONDS = 45 * 60, MAX_POINTS_PER_SECOND = 60, WIPE_FRACTION = 0.25;

function bonus(escaped) { return escaped >= 4 ? 2.0 : escaped === 3 ? 1.6 : escaped === 2 ? 1.3 : escaped === 1 ? 1.0 : 0; }

module.exports = async ({ params, context, logger }) => {
  const scores = Array.isArray(params.scores) ? params.scores.map(s => Math.max(0, Math.floor(Number(s) || 0))).slice(0, 4) : [];
  const escaped = Math.max(0, Math.min(scores.length, Math.floor(Number(params.escaped) || 0)));
  const seconds = Number(params.seconds) || 0, seed = Math.floor(Number(params.seed) || 0), test = params.test === true;
  if (scores.length === 0) return { ok: false, reason: "no scores" };
  if (!(seconds >= MIN_SECONDS) || seconds > MAX_SECONDS) return { ok: false, reason: "match length" };
  for (const s of scores) if (s > seconds * MAX_POINTS_PER_SECOND) return { ok: false, reason: "score too high for the time played" };

  // The match counts for the team the host is in, as the server has it (Teams.js), never a name the game sends.
  const save = new DataApi(context);
  let key = null, info = null;
  try {
    const got = await save.getProtectedItems(context.projectId, context.playerId, ["team"]);
    key = got.data.results.length ? got.data.results[0].value.id : null;
    if (key) {
      const t = await save.getCustomItems(context.projectId, key, ["info"]);
      info = t.data.results.length ? t.data.results[0].value : null;
    }
  } catch (e) { logger.warning("team read failed", { "error.message": e.message }); }
  if (!key || !info) return { ok: false, reason: "no team" };

  const boards = new LeaderboardsApi(context);   // server access: the team's entry isn't the caller's own
  const pre = test ? "test_" : "";
  let total = 0, matches = 0, lastSeed = null;
  try {
    const e = (await boards.getLeaderboardPlayerScore(context.projectId, pre + "teams_total", key)).data;
    total = e.score || 0;
    const m = typeof e.metadata === "string" ? JSON.parse(e.metadata) : (e.metadata || {});
    matches = m.matches || 0; lastSeed = m.seed;
  } catch (e) { /* first match on the board */ }
  if (lastSeed === seed) return { ok: false, reason: "this island's result is already in" };

  const average = matches > 0 ? total / matches : 0;
  let delta = escaped > 0 ? Math.round(scores.reduce((a, b) => a + b, 0) * bonus(escaped)) : -Math.round(average * WIPE_FRACTION);
  delta = Math.max(delta, -total) || 0; // never -0
  const meta = { crew: info.name, country: info.country, avatar: info.avatar, members: info.members.length,
                 matches: matches + 1, seed: seed, size: scores.length };
  // Always written, even 0, so a team that played an online match is on the board.
  const ids = [pre + "teams_total", pre + "teams_week"];
  if (!test && COUNTRIES.includes(info.country)) ids.push("teams_total_" + info.country);
  for (const board of ids) {
    try { await boards.addLeaderboardPlayerScore(context.projectId, board, key, { score: delta, metadata: meta }); }
    catch (e) { if (board === ids[0]) throw e; logger.warning("board", { "error.message": e.message }); }
  }
  return { ok: true, delta: delta, total: total + delta, team: info.name };
};

module.exports.params = {
  crew: "String", scores: "JSON", escaped: "Numeric", seconds: "Numeric", seed: "Numeric", test: "Boolean"
};
