import { copyFile, mkdir, readFile, readdir, realpath, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const destination = process.argv[2];
if (!destination) throw new Error('Usage: node scripts/collect-licenses.mjs <publish-directory>');
const output = path.join(await realpath(destination), 'licenses');
await mkdir(output, { recursive: true });
await copyFile(path.join(root, 'scripts/licenses/MIT.txt'), path.join(output, 'MIT.txt'));
await copyFile(path.join(root, 'scripts/licenses/Apache-2.0.txt'), path.join(output, 'Apache-2.0.txt'));

const index = ['# Packaged dependency notices', '', 'Package metadata and original license files are retained in the directories below.', ''];
const licenseName = /^(licen[sc]e|notice|copying|copyright|third[-_]party[-_]notices)([._-].*)?$/i;
const readJson = async (file) => JSON.parse(await readFile(file, 'utf8'));

async function collectPackage(source, relative, metadata, fileList) {
  const target = path.join(output, relative);
  await mkdir(target, { recursive: true });
  const files = fileList ?? (await readdir(source)).filter((name) => licenseName.test(name));
  let count = 0;
  for (const name of files) {
    if (!licenseName.test(path.basename(name))) continue;
    const file = path.join(source, name);
    if (!(await stat(file)).isFile()) continue;
    const targetFile = path.join(target, name);
    await mkdir(path.dirname(targetFile), { recursive: true });
    await copyFile(file, targetFile);
    count++;
  }
  await writeFile(path.join(target, 'metadata.txt'), metadata, 'utf8');
  index.push(`- ${relative}: ${count} original notice file(s); see metadata.txt for package license and copyright.`);
  return count;
}

const web = path.join(root, 'web');
const virtualStore = path.join(web, 'node_modules/.pnpm');
for (const entry of await readdir(virtualStore)) {
  const packageRoot = path.join(virtualStore, entry, 'node_modules');
  let packageNames;
  try {
    packageNames = await readdir(packageRoot);
  } catch (error) {
    if (error.code === 'ENOENT') continue;
    throw error;
  }
  for (const topLevelName of packageNames) {
    const topLevelPath = path.join(packageRoot, topLevelName);
    const sources = topLevelName.startsWith('@')
      ? (await readdir(topLevelPath)).map((name) => path.join(topLevelPath, name))
      : [topLevelPath];
    for (const source of sources) {
      const packageJson = await readJson(path.join(source, 'package.json'));
  const name = packageJson.name.replaceAll('/', '__');
  const metadata = JSON.stringify({
    name: packageJson.name,
    version: packageJson.version,
    license: packageJson.license,
    author: packageJson.author,
    repository: packageJson.repository,
    copyright: packageJson.copyright,
  }, null, 2);
  const relative = `npm/${name}/${packageJson.version}`;
  const count = await collectPackage(source, relative, metadata);
  if (count === 0 && packageJson.name === 'saxes' && packageJson.version === '6.0.0') {
    await copyFile(path.join(root, 'scripts/licenses/saxes-6.0.0-LICENSE'), path.join(output, relative, 'LICENSE'));
  } else if (count === 0 && packageJson.license !== 'MIT' && packageJson.license !== 'Apache-2.0') {
    throw new Error(`No license text for ${packageJson.name}@${packageJson.version}`);
  }
    }
  }
}

const assets = await readJson(path.join(root, 'server/obj/project.assets.json'));
const dependencies = await readJson(path.join(destination, 'Hsin.deps.json'));
const libraries = { ...assets.libraries };
for (const [identity, dependency] of Object.entries(dependencies.libraries)) {
  if (dependency.type !== 'runtimepack') continue;
  const packageIdentity = identity.replace(/^runtimepack\./, '');
  libraries[packageIdentity] = { type: 'package', path: packageIdentity.toLowerCase() };
}
for (const [identity, library] of Object.entries(libraries)) {
  if (library.type !== 'package') continue;
  let source;
  for (const folder of Object.keys(assets.packageFolders)) {
    const candidate = path.join(folder, library.path);
    try {
      if ((await stat(candidate)).isDirectory()) { source = candidate; break; }
    } catch (error) {
      if (error.code !== 'ENOENT') throw error;
    }
  }
  if (!source) throw new Error(`Restored package missing: ${identity}`);
  const files = library.files ?? await readdir(source);
  const nuspec = files.find((file) => file.endsWith('.nuspec'));
  if (!nuspec) throw new Error(`Package metadata missing: ${identity}`);
  const metadata = await readFile(path.join(source, nuspec), 'utf8');
  const count = await collectPackage(source, `nuget/${identity}`, metadata, files);
  if (count === 0 && !/<license\b[^>]*type="expression"[^>]*>\s*(MIT|Apache-2\.0)\s*<\/license>/.test(metadata)) {
    throw new Error(`No license text or supported license expression for ${identity}`);
  }
}

await writeFile(path.join(output, 'README.md'), `${index.join('\n')}\n`, 'utf8');
console.log(`Collected dependency notices in ${output}`);
