// Inputs: declared in this file's Unity import settings (the .meta), which is what deployment sends. The
// module.exports.params at the bottom documents them; Unity would only read those in an initialised JS project.
// Hushclaw — teams. A player is in at most one team: they create one, join one, or leave to join another.
// A team keeps its name, country, emblem and members in Cloud Save (game data, written only here), and its points
// on the teams boards (all time, this week, its country), keyed by its id so the totals follow the team.
//
//   action "mine"                       -> { ok, team }            the caller's team, or team: null
//   action "create" name country avatar -> { ok, team } | reason   the caller founds a team (and leaves theirs)
//   action "join"   id                  -> { ok, team } | reason   the caller joins a team (and leaves theirs)
//   action "leave"                      -> { ok }
//
// Finding teams is a read of the teams boards (the client reads them; each entry's metadata carries the name,
// country, emblem and member count).
const { DataApi } = require("@unity-services/cloud-save-1.4");
const { LeaderboardsApi } = require("@unity-services/leaderboards-1.1");

const MAX_MEMBERS = 8, EMBLEMS = 12;
const COUNTRIES = ["US","GB","CA","AU","NZ","IE","DE","FR","ES","IT","NL","BE","SE","NO","DK","FI","PL","PT","BR","MX",
  "AR","CL","RU","UA","TR","IN","PK","BD","LK","SG","MY","ID","PH","TH","VN","JP","KR","TW","HK","CN","SA","AE","EG",
  "NG","ZA"];

// "crew-" ids keep the totals the same name earned while teams were still called crews.
function teamId(name) {
  const slug = String(name || "").toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 40);
  return slug ? "crew-" + slug : null;
}
function cleanName(name) { return String(name || "").replace(/[^A-Za-z0-9 '\-]/g, "").replace(/\s+/g, " ").trim().slice(0, 24); }

async function readTeam(save, projectId, id) {
  const got = await save.getCustomItems(projectId, id, ["info"]);
  const r = got.data.results.length ? got.data.results[0] : null;
  return r ? { info: r.value, lock: r.writeLock } : null;
}

async function writeTeam(save, projectId, id, info, lock) {
  await save.setCustomItem(projectId, id, lock ? { key: "info", value: info, writeLock: lock } : { key: "info", value: info });
}

// The boards' copy of the team's details (score 0: details only).
async function publish(boards, projectId, id, info, pre) {
  let old = {};
  try {
    const e = (await boards.getLeaderboardPlayerScore(projectId, pre + "teams_total", id)).data;
    old = typeof e.metadata === "string" ? JSON.parse(e.metadata) : (e.metadata || {});
  } catch (e) { /* not on the board yet */ }
  const meta = Object.assign({}, old, { crew: info.name, country: info.country, avatar: info.avatar, members: info.members.length });
  const ids = [pre + "teams_total", pre + "teams_week"];
  if (!pre && COUNTRIES.includes(info.country)) ids.push("teams_total_" + info.country);
  for (const b of ids) {
    try { await boards.addLeaderboardPlayerScore(projectId, b, id, { score: 0, metadata: meta }); }
    catch (e) { /* a missing country board must not stop the rest */ }
  }
}

function view(id, info) {
  return { id: id, name: info.name, country: info.country, avatar: info.avatar, members: info.members };
}

module.exports = async ({ params, context, logger }) => {
  const save = new DataApi(context);          // server access: team records and the caller's protected team
  const boards = new LeaderboardsApi(context);
  const pid = context.projectId, me = context.playerId;
  const action = String(params.action || "mine");
  const pre = params.test === true ? "test_" : "";
  const myName = String(params.player || "").slice(0, 20) || "Survivor";

  let mine = null;
  try {
    const got = await save.getProtectedItems(pid, me, ["team"]);
    mine = got.data.results.length ? got.data.results[0].value : null;
  } catch (e) { logger.warning("team read failed", { "error.message": e.message }); }

  async function leave() {
    if (!mine || !mine.id) return;
    const t = await readTeam(save, pid, mine.id);
    if (t) {
      t.info.members = t.info.members.filter(m => m.id !== me);
      await writeTeam(save, pid, mine.id, t.info, t.lock);
      await publish(boards, pid, mine.id, t.info, pre);
    }
    await save.setProtectedItem(pid, me, { key: "team", value: { id: null } });
    mine = null;
  }

  async function enter(id, info, lock) {
    info.members = info.members.filter(m => m.id !== me);
    info.members.push({ id: me, name: myName });
    await writeTeam(save, pid, id, info, lock);
    await save.setProtectedItem(pid, me, { key: "team", value: { id: id, name: info.name } });
    await publish(boards, pid, id, info, pre);
    mine = { id: id, name: info.name };
    return { ok: true, team: view(id, info) };
  }

  if (action === "mine") {
    if (!mine || !mine.id) return { ok: true, team: null };
    const t = await readTeam(save, pid, mine.id);
    return { ok: true, team: t ? view(mine.id, t.info) : null };
  }

  if (action === "leave") { await leave(); return { ok: true }; }

  if (action === "create") {
    const name = cleanName(params.name);
    const id = teamId(name);
    if (name.length < 3 || !id) return { ok: false, reason: "Team names need 3 to 24 letters or digits." };
    if (await readTeam(save, pid, id)) return { ok: false, reason: "That team name is taken. Join it, or pick another." };
    const country = String(params.country || "").toUpperCase().slice(0, 2);
    const avatar = Math.max(0, Math.min(EMBLEMS - 1, Math.floor(Number(params.avatar) || 0)));
    await leave();
    return await enter(id, { name: name, country: country, avatar: avatar, members: [], founder: me, created: Date.now() }, null);
  }

  if (action === "join") {
    const id = String(params.id || "");
    const t = id ? await readTeam(save, pid, id) : null;
    if (!t) return { ok: false, reason: "That team doesn't exist any more." };
    if (mine && mine.id === id) return { ok: true, team: view(id, t.info) };
    if (t.info.members.filter(m => m.id !== me).length >= MAX_MEMBERS) return { ok: false, reason: "That team is full." };
    await leave();
    const fresh = await readTeam(save, pid, id); // leaving may have touched nothing here, but read the latest
    try { return await enter(id, fresh.info, fresh.lock); }
    catch (e) { return { ok: false, reason: "Someone else joined at the same moment. Try again." }; }
  }

  return { ok: false, reason: "unknown action" };
};

module.exports.params = {
  action: "String", name: "String", country: "String", avatar: "Numeric", id: "String", player: "String", test: "Boolean"
};
